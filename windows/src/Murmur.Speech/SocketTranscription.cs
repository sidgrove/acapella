using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// One dictation streamed over one WebSocket to a cloud speech-to-text service. The
/// plumbing every provider shares; each subclass only says what its messages look like.
/// </summary>
/// <remarks>
/// <para>
/// The socket opens in the background the moment the key goes down; audio appended before
/// it is ready waits in a queue. At the key-up the rest of the audio goes, then half a
/// second of silence, then whatever tells the service the dictation is over, and the final
/// transcript is awaited. Any failure resolves to null and the dictation falls back to the
/// local model; nothing here ever throws at the caller.
/// </para>
/// <para>
/// The silence matters: without it Gemini cut the last word off, "non-resident" coming
/// back as "non-res" and "director" as "direct" on 2026-09-24.
/// </para>
/// </remarks>
internal abstract class SocketTranscription : IStreamingTranscription
{
    /// <summary>How long the socket and handshake may take before the dictation falls back to the local model.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Audio is sent in messages of about 100 ms, as the services recommend.</summary>
    protected const int SamplesPerMessage = AudioChunk.SampleRate / 10;

    /// <summary>Silence sent after the dictation so its last word is not cut off.</summary>
    private const int TrailingSilenceSamples = AudioChunk.SampleRate / 2;

    private readonly Channel<short[]> _audio = Channel.CreateUnbounded<short[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<string?> _final = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop = new();
    private readonly ClientWebSocket _socket = new();
    private readonly StringBuilder _text = new();
    private Task _run = Task.CompletedTask;
    private volatile bool _ended;

    /// <summary>Where to connect.</summary>
    protected abstract Uri Endpoint { get; }

    /// <summary>Adds authentication and anything else the connection request needs.</summary>
    protected abstract void Configure(ClientWebSocketOptions options);

    /// <summary>Messages sent straight after connecting, before the service says it is ready.</summary>
    protected virtual IEnumerable<ReadOnlyMemory<byte>> Opening() => [];

    /// <summary>Whether the first message from the service says it is ready for audio.</summary>
    protected abstract bool IsReady(JsonElement message);

    /// <summary>Messages sent once the service is ready, before the first audio.</summary>
    protected virtual IEnumerable<ReadOnlyMemory<byte>> Starting() => [];

    /// <summary>One audio message. <paramref name="last"/> is the final one of the dictation.</summary>
    protected abstract ReadOnlyMemory<byte> Audio(byte[] pcm16, bool last);

    /// <summary>Messages sent after the last audio to end the dictation.</summary>
    protected virtual IEnumerable<ReadOnlyMemory<byte>> Closing() => [];

    /// <summary>
    /// Reads one message: words of the final transcript, whether the transcript is now
    /// complete, and an error the service reported.
    /// </summary>
    protected abstract (string? Words, bool Complete, string? Error) Read(JsonElement message);

    /// <summary>Starts the socket. Called once, by the subclass's constructor, when its fields are set.</summary>
    protected void Begin() => _run = Task.Run(RunAsync);

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public void Append(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty || _ended) return;
        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++) pcm[i] = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
        _audio.Writer.TryWrite(pcm);
    }

    /// <inheritdoc />
    public async Task<string?> FinishAsync(CancellationToken cancellationToken)
    {
        _ended = true;
        _audio.Writer.TryComplete();
        try
        {
            return await _final.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LastError ??= "no final transcript in time";
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _ended = true;
        _audio.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _run.ConfigureAwait(false); }
        catch (Exception) { /* recorded in LastError */ }
        _socket.Dispose();
        _stop.Dispose();
    }

    /// <summary>Writes one JSON message.</summary>
    protected static ReadOnlyMemory<byte> Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer)) write(json);
        return buffer.WrittenMemory;
    }

    private async Task RunAsync()
    {
        var token = _stop.Token;
        try
        {
            Configure(_socket.Options);
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connect.CancelAfter(ConnectTimeout);
                await _socket.ConnectAsync(Endpoint, connect.Token).ConfigureAwait(false);
                foreach (var message in Opening()) await SendAsync(message, connect.Token).ConfigureAwait(false);
                using var first = await ReceiveAsync(connect.Token).ConfigureAwait(false);
                if (first is null || !IsReady(first.RootElement))
                {
                    Fail($"refused: {Describe(first)}");
                    return;
                }
            }

            foreach (var message in Starting()) await SendAsync(message, token).ConfigureAwait(false);
            var receiving = ReceiveLoopAsync(token);
            await SendAudioAsync(token).ConfigureAwait(false);
            foreach (var message in Closing()) await SendAsync(message, token).ConfigureAwait(false);
            await receiving.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _final.TrySetResult(null);
        }
        catch (OperationCanceledException)
        {
            Fail($"no connection within {ConnectTimeout.TotalSeconds:0} s");
        }
        catch (Exception e) when (e is WebSocketException or JsonException or InvalidOperationException or IOException)
        {
            Fail(_socket.CloseStatusDescription is { Length: > 0 } reason ? $"{e.Message} ({reason})" : e.Message);
        }
        finally
        {
            _final.TrySetResult(null);
            if (_socket.State == WebSocketState.Open)
            {
                try
                {
                    using var close = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, close.Token).ConfigureAwait(false);
                }
                catch (Exception) { /* closing is best effort */ }
            }
        }
    }

    /// <summary>Sends queued audio in messages of about 100 ms until the dictation ends, then the silence.</summary>
    private async Task SendAudioAsync(CancellationToken token)
    {
        var pending = new List<short>(SamplesPerMessage * 2);
        var reader = _audio.Reader;
        while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var chunk)) pending.AddRange(chunk);
            while (pending.Count >= SamplesPerMessage) await SendAudioMessageAsync(pending, SamplesPerMessage, last: false, token).ConfigureAwait(false);
        }
        pending.AddRange(new short[TrailingSilenceSamples]);
        while (pending.Count > 0)
        {
            var count = Math.Min(pending.Count, SamplesPerMessage);
            await SendAudioMessageAsync(pending, count, last: count == pending.Count, token).ConfigureAwait(false);
        }
    }

    private Task SendAudioMessageAsync(List<short> pending, int count, bool last, CancellationToken token)
    {
        var bytes = new byte[count * 2];
        for (var i = 0; i < count; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), pending[i]);
        pending.RemoveRange(0, count);
        return SendAsync(Audio(bytes, last), token);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (true)
        {
            using var message = await ReceiveAsync(token).ConfigureAwait(false);
            if (message is null)
            {
                Fail(_socket.CloseStatusDescription is { Length: > 0 } reason ? $"closed: {reason}" : "closed before the final transcript");
                return;
            }

            var (words, complete, error) = Read(message.RootElement);
            if (error is not null)
            {
                Fail(error);
                return;
            }
            if (!string.IsNullOrWhiteSpace(words))
            {
                if (_text.Length > 0) _text.Append(' ');
                _text.Append(words.Trim());
            }
            if (complete && _ended)
            {
                LastError = null;
                _final.TrySetResult(_text.ToString());
                return;
            }
        }
    }

    private Task SendAsync(ReadOnlyMemory<byte> json, CancellationToken token) =>
        _socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, token).AsTask();

    /// <summary>One whole message as JSON, or null once the socket has closed.</summary>
    private async Task<JsonDocument?> ReceiveAsync(CancellationToken token)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer.GetMemory(4096), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            buffer.Advance(result.Count);
            if (result.EndOfMessage) return JsonDocument.Parse(buffer.WrittenMemory);
        }
    }

    private void Fail(string reason)
    {
        LastError = reason;
        _final.TrySetResult(null);
    }

    private string Describe(JsonDocument? message)
    {
        if (message is null) return _socket.CloseStatusDescription is { Length: > 0 } reason ? reason : "the connection closed";
        var text = message.RootElement.GetRawText();
        return text.Length > 200 ? text[..200] : text;
    }
}
