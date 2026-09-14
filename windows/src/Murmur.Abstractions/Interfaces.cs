namespace Murmur.Abstractions;

/// <summary>
/// A block of captured audio: mono, 32-bit float, 16 kHz, samples in [-1, 1].
/// </summary>
/// <remarks>
/// The format is fixed at this boundary on purpose. Every speech model this app will use
/// wants 16 kHz mono float, and resampling is a device concern — so the platform layer deals
/// with whatever the hardware offers and nothing above it ever has to ask.
/// </remarks>
/// <param name="Samples">Owned by the receiver. The producer must not reuse the buffer.</param>
public readonly record struct AudioChunk(ReadOnlyMemory<float> Samples)
{
    /// <summary>The sample rate every implementation must deliver.</summary>
    public const int SampleRate = 16000;

    /// <summary>Duration of this chunk.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);

    /// <summary>
    /// Root-mean-square amplitude, 0…1. Drives the level meter.
    /// </summary>
    public float Rms()
    {
        var span = Samples.Span;
        if (span.Length == 0) return 0;

        double sum = 0;
        foreach (var sample in span) sum += (double)sample * sample;
        return (float)Math.Sqrt(sum / span.Length);
    }
}

/// <summary>Captures microphone audio.</summary>
/// <remarks>
/// Implementations must deliver <see cref="AudioChunk"/>s already converted to 16 kHz mono
/// float, and must hand over buffers they will not touch again — the single most common
/// audio bug on every platform is a capture callback reusing one array.
/// </remarks>
public interface IAudioCapture : IAsyncDisposable
{
    /// <summary>Whether capture is currently running.</summary>
    bool IsCapturing { get; }

    /// <summary>
    /// True when the device appears to be delivering digital silence.
    /// </summary>
    /// <remarks>
    /// On Windows, when "Let desktop apps access your microphone" is off, capture does not
    /// fail — it yields exact zeros. That has to reach the user as a sentence about the
    /// privacy setting rather than as an empty transcript, so implementations report it here.
    /// </remarks>
    bool LooksLikeBlockedMicrophone { get; }

    /// <summary>Starts capture and yields chunks until cancelled.</summary>
    IAsyncEnumerable<AudioChunk> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// How much of the most recent capture was recorded before it was asked for — audio
    /// from before the key press that a warm implementation keeps. Zero for a plain device.
    /// </summary>
    TimeSpan PreRollDelivered => TimeSpan.Zero;
}

/// <summary>One microphone the OS knows about.</summary>
/// <param name="Id">Stable device identifier, stored in settings.</param>
/// <param name="Name">What to show the user.</param>
/// <param name="IsDefault">Whether this is the device Windows would pick on its own.</param>
public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>Lists microphones, so the user can pick one rather than trust the OS default.</summary>
public interface IAudioDeviceCatalog
{
    /// <summary>Every active capture device, default first.</summary>
    IReadOnlyList<AudioDevice> ListCaptureDevices();
}

/// <summary>Modifier flags for a chord, stored as an int in settings.</summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Either Ctrl.</summary>
    Control = 1,

    /// <summary>Either Shift.</summary>
    Shift = 2,

    /// <summary>Either Alt.</summary>
    Alt = 4,

    /// <summary>Either Windows key.</summary>
    Windows = 8,
}

/// <summary>Raised when the push-to-talk key goes down or comes up.</summary>
public interface IHotkeySource : IDisposable
{
    /// <summary>The key is held.</summary>
    event EventHandler? Pressed;

    /// <summary>The key was released.</summary>
    event EventHandler? Released;

    /// <summary>
    /// Escape was pressed. The engine discards a recording in progress; at any other time
    /// this is ignored. The key is never swallowed.
    /// </summary>
    event EventHandler? CancelPressed;

    /// <summary>The virtual-key code being watched. Read on every event, so a change applies to the next press.</summary>
    int VirtualKey { get; set; }

    /// <summary>
    /// Modifiers that must be held when <see cref="VirtualKey"/> goes down, as
    /// <see cref="HotkeyModifiers"/> flags. Zero for a bare key.
    /// </summary>
    int Modifiers { get; set; }

    /// <summary>
    /// Raised once after <see cref="BeginCapture"/> with the key and modifiers the user
    /// pressed. Fired from the hook's thread.
    /// </summary>
    event EventHandler<(int VirtualKey, int Modifiers)>? Captured;

    /// <summary>
    /// Records the next chord instead of triggering on it.
    /// </summary>
    /// <remarks>
    /// Done here, at the hook, rather than in a window: a window never sees Win+key (the
    /// shell takes it first) and Ctrl combinations collide with the app's own shortcuts. The
    /// hook sees everything, and while capturing it swallows the keys so Win+E does not also
    /// open Explorer.
    /// </remarks>
    void BeginCapture();

    /// <summary>
    /// Raised when a capture begun with <see cref="BeginCapture"/> ends without a chord:
    /// the user pressed Escape. Fired from the hook's thread. Optional for implementations
    /// that cannot observe Escape.
    /// </summary>
    event EventHandler? CaptureCancelled { add { } remove { } }

    /// <summary>Abandons a capture in progress.</summary>
    void CancelCapture();

    /// <summary>
    /// Whether the trigger key is physically down right now, where the platform can tell.
    /// </summary>
    /// <remarks>
    /// A low-level hook never sees a key-up delivered to an elevated window or the secure
    /// desktop. Without this the engine would keep recording until the next press. Returns
    /// true where the platform cannot observe key state, so nothing is ever cut short.
    /// </remarks>
    bool IsTriggerHeld => true;

    /// <summary>Begins listening.</summary>
    /// <returns>False if the hook could not be installed.</returns>
    bool Start();

    /// <summary>Stops listening.</summary>
    /// <remarks>Not named <c>Stop</c>: that is a reserved word in VB, which CA1716 flags
    /// on any interface member.</remarks>
    void StopListening();
}

/// <summary>Types text into whatever application currently has focus.</summary>
public interface ITextInjector
{
    /// <summary>Inserts <paramref name="text"/> at the caret of the focused control.</summary>
    /// <returns>False if the text could not be delivered.</returns>
    ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken);

    /// <summary>Presses Enter in the focused app after successful text delivery.</summary>
    ValueTask<bool> SendAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

/// <summary>Turns audio into text.</summary>
public interface ITranscriber : IAsyncDisposable
{
    /// <summary>Whether the model is loaded and ready.</summary>
    bool IsReady { get; }

    /// <summary>Loads the model. Slow — call once, at startup or first use.</summary>
    ValueTask<bool> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Transcribes one utterance.</summary>
    /// <param name="samples">16 kHz mono float, in [-1, 1].</param>
    /// <param name="biasPhrases">
    /// Dictionary terms to bias the recogniser toward. May be ignored by engines that don't
    /// support it — the correction pass is what actually guarantees spelling.
    /// </param>
    /// <param name="cancellationToken">Cancels a transcription in flight.</param>
    ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken);
}

/// <summary>
/// Registers the app to start when the user signs in.
/// </summary>
/// <remarks>
/// A tray-resident dictation app that has to be launched by hand every morning is not
/// really resident. Implemented per platform; where no implementation is found the toggle
/// is simply not offered.
/// </remarks>
public interface IStartupRegistration
{
    /// <summary>Whether the app is currently registered to start at sign-in.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers or unregisters the running executable.</summary>
    /// <returns>False if the registration could not be changed.</returns>
    bool SetEnabled(bool enabled);
}

/// <summary>
/// Small adjustments to a native window that the UI framework does not expose.
/// </summary>
public interface IWindowTweaks
{
    /// <summary>
    /// Stops a window from ever taking keyboard focus, even when clicked.
    /// </summary>
    /// <remarks>
    /// The overlay readout shows while the user is dictating into <i>another</i> app. If it
    /// could be activated, a stray click would move focus and the text would have nowhere to
    /// go — the same load-bearing rule as the macOS HUD panel.
    /// </remarks>
    /// <param name="handle">The platform window handle.</param>
    void MakeNonActivating(nint handle);

    /// <summary>
    /// Puts a window back into the always-on-top band of the z-order.
    /// </summary>
    /// <remarks>
    /// <c>WS_EX_TOPMOST</c> is a style bit, not a guarantee. On 2026-09-11 the pill still
    /// carried the bit yet sat under 140-odd ordinary windows, along with every other app's
    /// topmost overlay, after the process had run for fourteen hours. The framework only
    /// applies the band when <c>Topmost</c> changes, so the pill must re-assert it itself each
    /// time it is presented.
    /// </remarks>
    /// <param name="handle">The platform window handle.</param>
    void KeepOnTop(nint handle);

    /// <summary>
    /// The centre of the window the user is working in, in screen pixels, or null.
    /// </summary>
    /// <remarks>
    /// The overlay must appear on the monitor where the text is going, which is the one
    /// holding the foreground window — not the primary monitor, and not wherever the app's
    /// own window happens to be.
    /// </remarks>
    (int X, int Y)? ActiveWindowCentre();
}

/// <summary>
/// Mutes other applications on the current playback output while recording.
/// </summary>
/// <remarks>
/// <para>
/// Uses per-application mute and preserves volume levels. Apps already muted remain muted after restoration.
/// </para>
/// <para>
/// Both calls are best-effort and must never throw; a device that cannot be reached
/// simply means nothing is ducked. <see cref="Restore"/> is idempotent.
/// </para>
/// </remarks>
public interface IAudioDucker
{
    /// <summary>Mutes other applications' playback and remembers their mute states.</summary>
    void Duck();

    /// <summary>Restores the mute states saved by <see cref="Duck"/>.</summary>
    void Restore();
}

/// <summary>
/// Rewrites a transcript into the text the speaker meant to type.
/// </summary>
/// <remarks>
/// The optional generative tier: fillers out, spoken corrections applied, punctuation and
/// case fixed, meaning untouched. The raw path never depends on it — a cleaner that fails
/// returns null and the local text is typed instead.
/// </remarks>
public interface ITranscriptCleaner
{
    /// <summary>A short name for the history and the log, e.g. "gemini-2.5-flash".</summary>
    string Name { get; }

    /// <summary>Cleans <paramref name="text"/>, or returns null if it could not.</summary>
    Task<string?> CleanAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Cleans <paramref name="text"/>, the continuation of a dictation whose earlier part has
    /// already been cleaned to <paramref name="precedingCleaned"/>. Only the continuation is
    /// returned; the earlier text is context for names, tense and sentences that carry on.
    /// </summary>
    /// <remarks>
    /// This is what lets a long dictation be cleaned while it is still being spoken, so the
    /// wait after the key-up is for the last few seconds of speech, not for all of it.
    /// </remarks>
    Task<string?> CleanAsync(string text, string? precedingCleaned, CancellationToken cancellationToken) => CleanAsync(text, cancellationToken);

    /// <summary>Why the most recent <see cref="CleanAsync(string, CancellationToken)"/> returned null, for the log. Null when it succeeded.</summary>
    string? LastError => null;

    /// <summary>
    /// Opens whatever connection <see cref="CleanAsync(string, CancellationToken)"/> will need, so the round trip after
    /// the key is released does not also pay for a TCP and TLS handshake.
    /// </summary>
    /// <remarks>Called when recording starts. Must never throw; failures are for the log.</remarks>
    Task WarmUpAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Wall-clock time, behind an interface so timing logic is testable.
/// </summary>
/// <remarks>
/// Anything that measures a duration takes one of these. A test that depends on the real
/// clock is a test that fails on a slow CI runner.
/// </remarks>
public interface IClock
{
    /// <summary>The current instant.</summary>
    DateTimeOffset Now { get; }
}

/// <inheritdoc cref="IClock"/>
public sealed class SystemClock : IClock
{
    /// <summary>The shared instance.</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.Now;
}
