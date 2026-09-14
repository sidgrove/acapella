using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Keeps the microphone open between dictations and hands each new recording the audio
/// captured just before the key was pressed.
/// </summary>
/// <remarks>
/// <para>
/// Opening a WASAPI stream on the key press means the first word is at the mercy of the
/// device: a USB interface can take a good fraction of a second before real samples arrive,
/// and anyone who starts talking as they press loses the start of the sentence. Reported on
/// 2026-09-11 as "it's not picking up the beginning".
/// </para>
/// <para>
/// So after the first recording the inner capture is left running. While no recording is in
/// progress the newest <see cref="PreRoll"/> of audio is kept in a ring; when the key is
/// pressed the ring is delivered first, then live audio, with no device start-up at all.
/// After <see cref="IdleTimeout"/> without a recording the stream is closed, so the
/// microphone indicator does not stay lit all day.
/// </para>
/// <para>
/// <b>One pump at a time.</b> The inner capture is a single device object and cannot be
/// opened twice. A pump that is winding down — after the idle timeout, or because the user
/// picked a different microphone — is awaited before a new one starts, and a pump that is no
/// longer current never completes a session or clears a ring that now belongs to its
/// successor. The first version had exactly that race, and the dictation that landed on the
/// five-minute boundary was lost without a word.
/// </para>
/// <para>
/// This lives in Core, not the platform layer, so it runs against the fake capture in CI.
/// </para>
/// </remarks>
public sealed class WarmAudioCapture : IAudioCapture
{
    /// <summary>How much audio from before the key press is included in a recording.</summary>
    /// <remarks>
    /// Long enough to cover a first word spoken as the fingers land on the keys. The model
    /// also decodes a word better with some room tone in front of it.
    /// </remarks>
    public static readonly TimeSpan DefaultPreRoll = TimeSpan.FromMilliseconds(600);

    /// <summary>How long the microphone stays open after a recording with no new one.</summary>
    /// <remarks>
    /// Never, by default. A five-minute timeout meant nearly every real dictation was a cold
    /// start, and a cold Elgato stream delivered 200-900 ms of silence before the first
    /// sample with any sound in it (log, 2026-09-11). The device is released only when
    /// dictation is switched off or the app exits.
    /// </remarks>
    public static readonly TimeSpan DefaultIdleTimeout = Timeout.InfiniteTimeSpan;

    private readonly IAudioCapture _inner;
    private readonly int _preRollSamples;
    private readonly TimeSpan _idleTimeout;
    private readonly Lock _lock = new();
    private readonly Queue<float[]> _ring = new();
    private int _ringSamples;

    private Channel<AudioChunk>? _session;
    private CancellationTokenSource? _pump;
    private Task? _pumpTask;
    private CancellationTokenSource? _idle;
    private bool _reopenWanted;
    private bool _rewarmAfterRelease;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public WarmAudioCapture(IAudioCapture inner, TimeSpan? preRoll = null, TimeSpan? idleTimeout = null)
    {
        _inner = inner;
        PreRoll = preRoll ?? DefaultPreRoll;
        IdleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _preRollSamples = (int)(PreRoll.TotalSeconds * AudioChunk.SampleRate);
        _idleTimeout = IdleTimeout;
    }

    /// <summary>Audio kept from before the key press.</summary>
    public TimeSpan PreRoll { get; }

    /// <summary>Time without a recording before the microphone is released.</summary>
    public TimeSpan IdleTimeout { get; }

    /// <summary>Whether the inner device is open, recording or not.</summary>
    public bool IsWarm => _pump is not null && _pumpTask is { IsCompleted: false };

    /// <inheritdoc />
    public bool IsCapturing => _session is not null;

    /// <inheritdoc />
    public bool LooksLikeBlockedMicrophone => _inner.LooksLikeBlockedMicrophone;

    /// <inheritdoc />
    public TimeSpan PreRollDelivered { get; private set; }

    /// <summary>
    /// Opens the device now, before any key press, so the first recording of the day is as
    /// warm as the tenth. Safe to call repeatedly; does nothing while already warm.
    /// </summary>
    public void WarmUp() => _ = WarmUpAsync();

    private async Task WarmUpAsync()
    {
        Task? draining;
        lock (_lock) draining = _pump is null ? _pumpTask : null;
        if (draining is { IsCompleted: false })
        {
            try { await draining.ConfigureAwait(false); }
            catch (Exception) { /* reported when it happened */ }
        }

        lock (_lock)
        {
            if (_session is not null || IsWarm) return;
            _reopenWanted = false;
            StartPump();
            ScheduleRelease();
        }
        Log.Info("microphone opened ahead of the first recording");
    }

    /// <summary>
    /// Closes the device and leaves it closed — when dictation is switched off. A recording
    /// in progress keeps the device until it ends.
    /// </summary>
    public void Release()
    {
        CancellationTokenSource? pump = null;
        lock (_lock)
        {
            _reopenWanted = true;
            _rewarmAfterRelease = false;
            if (_session is null) pump = ReleaseLocked();
        }
        pump?.Cancel();
        if (pump is not null) Log.Info("microphone released: dictation off");
    }

    /// <summary>
    /// Closes the device and opens it again — after the user picks a different microphone
    /// in Settings.
    /// </summary>
    /// <remarks>
    /// The inner capture reads the chosen device id only when it opens, so a warm stream
    /// would otherwise keep recording from the old microphone until the idle timeout. If a
    /// recording is in progress the reopen waits until it ends; that recording keeps the
    /// device it started with.
    /// </remarks>
    public void ReopenDevice()
    {
        CancellationTokenSource? pump = null;
        lock (_lock)
        {
            _reopenWanted = true;
            _rewarmAfterRelease = true;
            if (_session is null) pump = ReleaseLocked();
        }
        pump?.Cancel();
        if (pump is not null) Log.Info("microphone released to switch device");
        if (pump is not null) WarmUp();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> CaptureAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // A pump that has been told to stop is still holding the device until its task
        // finishes. Wait for it rather than open the device twice.
        Task? draining;
        lock (_lock) draining = _pump is null ? _pumpTask : null;
        if (draining is { IsCompleted: false })
        {
            try { await draining.ConfigureAwait(false); }
            catch (Exception) { /* reported when it happened */ }
        }

        Channel<AudioChunk> session;
        lock (_lock)
        {
            if (_session is not null) throw new InvalidOperationException("A recording is already in progress.");

            _idle?.Cancel();
            _idle = null;

            // Bounded and drop-oldest for the same reason as the device capture: a slow
            // consumer must never stall the audio thread.
            session = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(512)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = true,
            });
            var delivered = 0;
            while (_ring.TryDequeue(out var kept))
            {
                session.Writer.TryWrite(new AudioChunk(kept));
                delivered += kept.Length;
            }
            PreRollDelivered = TimeSpan.FromSeconds((double)delivered / AudioChunk.SampleRate);
            _ringSamples = 0;
            _session = session;

            if (!IsWarm) StartPump();
        }

        try
        {
            await foreach (var chunk in session.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
        finally
        {
            CancellationTokenSource? pump = null;
            var rewarm = false;
            lock (_lock)
            {
                _session = null;
                rewarm = _rewarmAfterRelease;
                if (_reopenWanted) pump = ReleaseLocked();
                else if (IsWarm) ScheduleRelease();
            }
            pump?.Cancel();
            if (pump is not null && rewarm) WarmUp();
        }
    }

    private void StartPump()
    {
        var pump = new CancellationTokenSource();
        _pump = pump;
        _pumpTask = PumpAsync(pump, pump.Token);
    }

    private async Task PumpAsync(CancellationTokenSource identity, CancellationToken cancellationToken)
    {
        // True while this pump is the one the rest of the class is talking to. A pump that
        // has been replaced must not complete a session or touch a ring it no longer owns.
        bool Current() => ReferenceEquals(_pump, identity);

        try
        {
            await foreach (var chunk in _inner.CaptureAsync(cancellationToken).ConfigureAwait(false))
            {
                // Copied: the inner capture may reuse its buffer the moment this returns, and
                // the ring outlives that by up to PreRoll.
                var owned = chunk.Samples.ToArray();
                lock (_lock)
                {
                    if (!Current()) continue;
                    if (_session is { } session)
                    {
                        session.Writer.TryWrite(new AudioChunk(owned));
                    }
                    else
                    {
                        _ring.Enqueue(owned);
                        _ringSamples += owned.Length;
                        while (_ringSamples > _preRollSamples && _ring.Count > 1)
                        {
                            _ringSamples -= _ring.Dequeue().Length;
                        }
                    }
                }
            }

            // The inner stream ended on its own (a fake ran out, or a device stopped
            // cleanly). A recording waiting on it is over, and so is this pump.
            lock (_lock)
            {
                if (Current())
                {
                    _session?.Writer.TryComplete();
                    _pump = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_lock) if (Current()) _session?.Writer.TryComplete();
        }
        catch (Exception e)
        {
            // Mid-recording this surfaces to the engine exactly as a direct capture failure
            // would. Between recordings there is no one to tell, so log it and let the next
            // key press reopen the device.
            lock (_lock)
            {
                if (Current())
                {
                    if (_session is { } session) session.Writer.TryComplete(e);
                    else Log.Warn($"warm microphone stopped: {e.Message}");
                    _pump = null;
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                if (_pump is null || Current())
                {
                    _ring.Clear();
                    _ringSamples = 0;
                }
            }
        }
    }

    private void ScheduleRelease()
    {
        if (_idleTimeout == Timeout.InfiniteTimeSpan) return;
        var idle = new CancellationTokenSource();
        _idle = idle;
        _ = ReleaseAfterIdleAsync(idle.Token);
    }

    /// <summary>Marks the current pump as finished with. Call under the lock; cancel the result outside it.</summary>
    private CancellationTokenSource? ReleaseLocked()
    {
        _idle?.Cancel();
        _idle = null;
        _reopenWanted = false;
        _rewarmAfterRelease = false;
        _ring.Clear();
        _ringSamples = 0;
        var pump = _pump;
        _pump = null;
        return pump;
    }

    private async Task ReleaseAfterIdleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_idleTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        CancellationTokenSource? pump;
        lock (_lock)
        {
            if (_session is not null || cancellationToken.IsCancellationRequested) return;
            pump = ReleaseLocked();
        }
        pump?.Cancel();
        if (pump is not null) Log.Info("microphone released after idle");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task? pumpTask;
        CancellationTokenSource? pump;
        lock (_lock)
        {
            pump = ReleaseLocked();
            pumpTask = _pumpTask;
        }
        pump?.Cancel();
        if (pumpTask is not null)
        {
            try { await pumpTask.ConfigureAwait(false); }
            catch (Exception) { /* reported through the session already */ }
        }
        await _inner.DisposeAsync().ConfigureAwait(false);
    }
}
