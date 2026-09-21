using System.Runtime.CompilerServices;
using Murmur.Abstractions;

namespace Murmur.Testing;

/// <summary>
/// Test doubles for the four platform interfaces.
/// </summary>
/// <remarks>
/// These are what make the Windows app's behaviour verifiable from a machine that is not
/// Windows. Everything interesting — the state machine, chunking, the correction pass, the
/// silence case — runs against these in milliseconds, on any platform, in CI.
/// </remarks>
public sealed class FakeAudioCapture : IAudioCapture
{
    private readonly float[] _samples;
    private readonly int _chunkSize;

    /// <summary>Replays a fixed buffer as a series of chunks.</summary>
    /// <param name="samples">16 kHz mono float.</param>
    /// <param name="chunkSize">Samples per chunk; defaults to 20 ms.</param>
    public FakeAudioCapture(float[] samples, int chunkSize = AudioChunk.SampleRate / 50)
    {
        _samples = samples;
        _chunkSize = Math.Max(1, chunkSize);
    }

    /// <summary>Generates <paramref name="seconds"/> of a steady tone at <paramref name="amplitude"/>.</summary>
    public static FakeAudioCapture Tone(double seconds, float amplitude = 0.5f)
    {
        var count = (int)(seconds * AudioChunk.SampleRate);
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = amplitude * MathF.Sin(2 * MathF.PI * 440 * i / AudioChunk.SampleRate);
        }
        return new FakeAudioCapture(samples);
    }

    /// <summary>Generates <paramref name="seconds"/> of digital silence.</summary>
    public static FakeAudioCapture Silence(double seconds) =>
        new(new float[(int)(seconds * AudioChunk.SampleRate)]);

    /// <summary>
    /// Generates <paramref name="seconds"/> of deterministic noise: audible, and unlike a
    /// tone never repeats, so any stretch of it can be located in the whole by its first
    /// few samples. See <see cref="FakeTranscriber.ByOffset"/>.
    /// </summary>
    public static FakeAudioCapture Noise(double seconds, int seed = 7)
    {
        var random = new Random(seed);
        var samples = new float[(int)(seconds * AudioChunk.SampleRate)];
        for (var i = 0; i < samples.Length; i++) samples[i] = (float)(random.NextDouble() - 0.5) * 0.6f;
        return new FakeAudioCapture(samples);
    }

    /// <summary>The whole buffer this fake delivers.</summary>
    public ReadOnlyMemory<float> Samples => _samples;

    /// <inheritdoc />
    public bool IsCapturing { get; private set; }

    /// <summary>
    /// True once every chunk has been handed over. Monotonic, so a test can wait on it
    /// without racing the burst — the fake delivers its whole buffer in microseconds, and
    /// "the level went up and came back down" is not something a poller reliably sees.
    /// </summary>
    public bool Delivered => Deliveries > 0;

    /// <summary>
    /// How many times the whole buffer has been delivered. A test that records twice with
    /// one fake waits for the second delivery, not for <see cref="Delivered"/>, which the
    /// first recording already made true.
    /// </summary>
    public int Deliveries { get; private set; }

    /// <summary>Set to simulate the OS feeding silence because the microphone is blocked.</summary>
    public bool LooksLikeBlockedMicrophone { get; set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IsCapturing = true;
        try
        {
            for (var offset = 0; offset < _samples.Length; offset += _chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(_chunkSize, _samples.Length - offset);

                // A fresh array per chunk, deliberately: a fake that reuses one buffer would
                // hide the very aliasing bug real capture implementations are prone to.
                var chunk = new float[length];
                Array.Copy(_samples, offset, chunk, 0, length);
                yield return new AudioChunk(chunk);

                await Task.Yield();
            }

            Deliveries++;
        }
        finally
        {
            IsCapturing = false;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A hotkey you press from a test.</summary>
public sealed class FakeHotkeySource : IHotkeySource
{
    /// <inheritdoc />
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public event EventHandler? Released;

    /// <inheritdoc />
    public event EventHandler? CancelPressed;

    /// <summary>Raises <see cref="CancelPressed"/>.</summary>
    public void PressCancel() => CancelPressed?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public int VirtualKey { get; set; } = 0xA3;

    /// <inheritdoc />
    public int Modifiers { get; set; }

    /// <inheritdoc />
    public event EventHandler<(int VirtualKey, int Modifiers)>? Captured;

    /// <summary>Whether a capture is in progress.</summary>
    public bool IsCapturing { get; private set; }

    /// <inheritdoc />
    public void BeginCapture() => IsCapturing = true;

    /// <inheritdoc />
    public void CancelCapture() => IsCapturing = false;

    /// <summary>Simulates the user pressing a chord while capturing.</summary>
    public void Capture(int virtualKey, int modifiers)
    {
        IsCapturing = false;
        Captured?.Invoke(this, (virtualKey, modifiers));
    }

    /// <summary>Whether <see cref="Start"/> has been called.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Key presses a test says the user made. Bump it to mean "they typed something".</summary>
    public long UserKeyPresses { get; set; }

    /// <inheritdoc />
    public bool Start() { IsRunning = true; return true; }

    /// <inheritdoc />
    public void StopListening() => IsRunning = false;

    /// <summary>Raises <see cref="Pressed"/>.</summary>
    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="Released"/>.</summary>
    public void Release() => Released?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose() => StopListening();
}

/// <summary>Returns canned transcripts and records what it was asked to transcribe.</summary>
public sealed class FakeTranscriber : ITranscriber
{
    private readonly Queue<string> _responses;
    private readonly Func<ReadOnlyMemory<float>, string>? _bySamples;

    /// <summary>Each call returns the next response; the last repeats once exhausted.</summary>
    public FakeTranscriber(params string[] responses) => _responses = new Queue<string>(responses);

    private FakeTranscriber(Func<ReadOnlyMemory<float>, string> bySamples)
    {
        _responses = new Queue<string>();
        _bySamples = bySamples;
    }

    /// <summary>
    /// Answers by where the submitted audio sits in <paramref name="source"/> rather than by
    /// call order. The preview loop decodes on a timer while the fake capture is still
    /// delivering, so on a slow CI runner the order of calls is not the order a fast machine
    /// sees — but a segment that starts at the very beginning is always the frozen piece,
    /// and one that starts later is always the live tail.
    /// </summary>
    /// <param name="source">The whole buffer, from <see cref="FakeAudioCapture.Noise"/> so stretches are unique.</param>
    /// <param name="atStart">The transcript for audio that begins at the start of the buffer.</param>
    /// <param name="later">The transcript for audio that begins anywhere after it.</param>
    public static FakeTranscriber ByOffset(ReadOnlyMemory<float> source, string atStart, string later) =>
        new(samples => OffsetOf(source.Span, samples.Span) == 0 ? atStart : later);

    private static int OffsetOf(ReadOnlySpan<float> source, ReadOnlySpan<float> segment)
    {
        var probe = segment[..Math.Min(8, segment.Length)];
        return source.IndexOf(probe);
    }

    /// <summary>How many segments were submitted. Reveals chunking behaviour.</summary>
    public List<int> SegmentLengths { get; } = [];

    /// <summary>The bias list handed over on the most recent call.</summary>
    public IReadOnlyList<string> LastBias { get; private set; } = [];

    /// <inheritdoc />
    public bool IsReady { get; private set; }

    /// <inheritdoc />
    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken)
    {
        IsReady = true;
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken)
    {
        SegmentLengths.Add(samples.Length);
        LastBias = biasPhrases;
        if (_bySamples is not null) return ValueTask.FromResult(_bySamples(samples));
        var text = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        return ValueTask.FromResult(text);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Records what would have been typed.
/// </summary>
/// <remarks>
/// Asserting on intent rather than on an OS effect is deliberate: whether a keystroke really
/// lands in another application's text field is the one thing CI cannot observe, because
/// runners cannot take the foreground.
/// </remarks>
public sealed class RecordingTextInjector : ITextInjector
{
    /// <summary>Everything injected, in order.</summary>
    public List<string> Injected { get; } = [];

    /// <summary>The control a test says has focus. Null, as for a platform that cannot tell, by default.</summary>
    public string? FocusTarget { get; set; }

    /// <inheritdoc />
    public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        Injected.Add(text);
        return ValueTask.FromResult(true);
    }
}

/// <summary>A startup registration that only remembers.</summary>
public sealed class FakeStartupRegistration : IStartupRegistration
{
    /// <inheritdoc />
    public bool IsEnabled { get; private set; }

    /// <inheritdoc />
    public bool SetEnabled(bool enabled) { IsEnabled = enabled; return true; }
}

/// <summary>A ducker that only remembers what it was asked.</summary>
public sealed class FakeAudioDucker : IAudioDucker
{
    /// <summary>Every call, in order: "duck" or "restore".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>What <see cref="Duck"/> reports: the loudest other playback, 0…1.</summary>
    public float Peak { get; set; }

    /// <inheritdoc />
    public (float Peak, string? Source) Duck()
    {
        Calls.Add("duck");
        return (Peak, Peak > 0 ? "fake" : null);
    }

    /// <inheritdoc />
    public void Restore() => Calls.Add("restore");
}

/// <summary>A decision model that records every question and answers from a script.</summary>
public sealed class FakeDecisionModel : IDecisionModel
{
    /// <summary>Every call: the state and the question keys asked.</summary>
    public List<(string State, IReadOnlyList<string> Keys)> Calls { get; } = [];

    /// <summary>Answers by question key. Unlisted questions get no answer.</summary>
    public Dictionary<string, Decision> Answers { get; } = [];

    /// <summary>A gate a test can hold shut to prove nothing waits on the model.</summary>
    public TaskCompletionSource Release { get; } = new();

    /// <inheritdoc />
    public string Name => "fake-decisions";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Decision>?> DecideAsync(string state, IReadOnlyList<DecisionQuestion> questions, CancellationToken cancellationToken)
    {
        Calls.Add((state, questions.Select(q => q.Key).ToList()));
        await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return questions.Where(q => Answers.ContainsKey(q.Key)).Select(q => Answers[q.Key]).ToList();
    }
}

/// <summary>A clock you advance by hand.</summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Moves time forward.</summary>
    public void Advance(TimeSpan by) => Now += by;
}
