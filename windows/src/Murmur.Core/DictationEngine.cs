using Murmur.Abstractions;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>What the engine is doing right now.</summary>
public enum DictationState
{
    /// <summary>Waiting for the hotkey.</summary>
    Idle,

    /// <summary>The key is held; audio is being captured.</summary>
    Recording,

    /// <summary>The key is released; the utterance is being transcribed.</summary>
    Transcribing,
}

/// <summary>How the key starts and stops a recording.</summary>
public enum ActivationMode
{
    /// <summary>Hold the key down while talking.</summary>
    Hold,

    /// <summary>Tap to start, tap again to stop. The release is ignored.</summary>
    Tap,

    /// <summary>
    /// Both: a quick tap toggles, a longer hold is push-to-talk. Nothing to choose.
    /// </summary>
    Automatic,
}

/// <summary>What happens to a full stop at the very end of a dictation.</summary>
public enum TrailingFullStop
{
    /// <summary>Leave it.</summary>
    Keep,

    /// <summary>Drop it after a lone sentence; prose keeps it.</summary>
    DropAfterSingleSentence,

    /// <summary>Never end with one.</summary>
    Never,
}

/// <summary>One completed dictation.</summary>
/// <param name="At">When the key was released.</param>
/// <param name="AudioDuration">How long the key was held.</param>
/// <param name="ProcessingTime">Release to finished text.</param>
/// <param name="Text">The final text, after corrections, rules, clean-up and polish.</param>
/// <param name="Corrections">Dictionary corrections that fired.</param>
/// <param name="CleanedBy">The model that cleaned the text, or null.</param>
/// <param name="RawText">What the speech model heard, before any rules or clean-up.</param>
/// <param name="CleanupFailed">The AI tier was on but its answer was unusable, so <paramref name="Text"/> is the local result.</param>
/// <param name="Cleanup">Which path the text took through the AI tier.</param>
/// <param name="Audio">The recording the text came from, for the archive kept for accuracy testing.</param>
/// <param name="TranscribedBy">The cloud model whose words these are, or null for the local model's.</param>
/// <param name="LocalRawText">The local model's transcript, kept beside a cloud one for comparison.</param>
public sealed record DictationResult(
    DateTimeOffset At,
    TimeSpan AudioDuration,
    TimeSpan ProcessingTime,
    string Text,
    IReadOnlyList<AppliedCorrection> Corrections,
    string? CleanedBy = null,
    string? RawText = null,
    bool CleanupFailed = false,
    CleanupPath Cleanup = CleanupPath.Off,
    ReadOnlyMemory<float> Audio = default,
    string? TranscribedBy = null,
    string? LocalRawText = null);

/// <summary>How a dictation went through the AI tier. Every path is named so the log can say which one ran.</summary>
public enum CleanupPath
{
    /// <summary>The tier is off, or no cleaner is configured.</summary>
    Off,

    /// <summary>Too short to be worth a round trip; the local rules did the work.</summary>
    SkippedShort,

    /// <summary>Pieces were cleaned while the user was still talking, then only the tail.</summary>
    Piecewise,

    /// <summary>The whole dictation was cleaned in one call after the key-up.</summary>
    Whole,

    /// <summary>The tier was tried and its answer was unusable; the local text was typed.</summary>
    LocalFallback,
}

/// <summary>
/// The whole dictation flow: hotkey down, capture, hotkey up, transcribe, correct, inject.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately platform-neutral. It targets plain <c>net10.0</c>, so <c>CA1416</c> turns any
/// accidental Windows API call in here into a build error. Everything platform-specific
/// arrives through the interfaces it is constructed with.
/// </para>
/// <para>
/// That is what makes the interesting behaviour testable without Windows: hand it fakes and
/// the entire path — including chunking, the correction pass and the "nothing was said"
/// case — runs on any machine, in milliseconds.
/// </para>
/// <para>
/// <b>Every failure becomes a sentence.</b> The first real-hardware run showed why: both
/// async paths were fired with <c>_ = ...</c>, so a missing assembly, a blocked microphone or
/// a model that would not load simply vanished and the panel did nothing. Now each path
/// catches, logs, raises <see cref="Faulted"/>, and always returns to <see cref="DictationState.Idle"/>.
/// </para>
/// <para>
/// <b>Recording and transcribing overlap.</b> Each press opens a <see cref="Session"/> with its
/// own buffer, token and preview; releasing hands that session to a transcription queue and
/// the next press can start at once, while the previous words are still being cleaned up.
/// Transcriptions run one at a time, in order, so text lands in the order it was spoken.
/// Before this a tap during the Transcribing state did nothing, which read as "sometimes
/// it doesn't start".
/// </para>
/// </remarks>
public sealed class DictationEngine : IAsyncDisposable
{
    /// <summary>The message shown when the OS is feeding silence instead of the microphone.</summary>
    public const string BlockedMicrophoneMessage =
        "Windows is blocking microphone access. Turn on “Let desktop apps access your microphone” "
        + "in Settings → Privacy & security → Microphone.";

    /// <summary>The message shown when there is no model to transcribe with.</summary>
    public const string ModelMissingMessage =
        "Speech model not installed. Download it from Settings → Model.";

    private readonly IAudioCapture _capture;
    private readonly IHotkeySource _hotkey;
    private readonly ITranscriber _transcriber;
    private readonly ITextInjector _injector;
    private readonly IClock _clock;
    private readonly Func<IReadOnlyList<DictionaryEntry>> _dictionary;

    /// <summary>Serialises the start and end of recordings.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// One press-to-release. Everything that belongs to a single recording lives here, so
    /// a new recording can begin while the previous one is still being transcribed.
    /// </summary>
    private sealed class Session : IDisposable
    {
        public readonly List<float> Buffer = [];
        public readonly CancellationTokenSource Stop = new();
        public DateTimeOffset StartedAt;
        public bool Heard;
        public bool CaptureReady;
        public TimeSpan PreRoll;
        public Task? Preview;
        public Task? CaptureLoop;

        /// <summary>
        /// Other apps were playing loudly until the key press muted them, so the pre-roll
        /// is their sound, not the user's, and the final pass discards it.
        /// </summary>
        public bool OtherAudioWasPlaying;

        /// <summary>
        /// Audio the preview loop has already decoded and frozen, oldest first. The final
        /// transcription reuses this text and decodes only what came after
        /// <see cref="CommittedSamples"/>. Guarded by <c>_bufferLock</c>.
        /// </summary>
        public readonly List<CommittedPiece> Committed = [];
        public int CommittedSamples;

        /// <summary>
        /// Samples at the start of the buffer that belong to other apps' playback, not the
        /// user, and are skipped by the preview and the final pass alike. Known from the
        /// first chunk, so the pieces frozen later never contain them and are kept.
        /// </summary>
        public int SkipSamples;

        /// <summary>
        /// The text before the caret when the key went down, still being read on a pool
        /// thread. Null when the setting is off.
        /// </summary>
        public Task<string?>? CaretContext;

        /// <summary>The cloud transcription fed while the key is held, or null.</summary>
        public IStreamingTranscription? Cloud;

        /// <summary>How much of <see cref="Buffer"/> has been handed to <see cref="Cloud"/>.</summary>
        public int CloudSent;

        public void Dispose()
        {
            Stop.Dispose();
            if (Cloud is { } cloud) _ = cloud.DisposeAsync().AsTask();
        }
    }

    /// <summary>A stretch of the recording whose transcript will not change.</summary>
    /// <param name="EndSample">Where in the buffer it ends.</param>
    /// <param name="Raw">Its transcript.</param>
    /// <param name="Clean">Its clean-up, started while the user was still talking; null when the AI tier is off.</param>
    private sealed record CommittedPiece(int EndSample, string Raw, Task<SegmentClean?>? Clean);

    /// <summary>The cleaned form of one committed piece, plus everything cleaned before it.</summary>
    private sealed record SegmentClean(string Local, string Cleaned, IReadOnlyList<AppliedCorrection> Applied, string CleanedSoFar);

    /// <summary>The recording in progress, or null. Written only under <see cref="_bufferLock"/>.</summary>
    private Session? _current;

    /// <summary>
    /// Guards <see cref="_current"/> and the current session's buffer. The capture loop
    /// appends on the audio thread while the preview snapshots on a pool thread; a
    /// <c>List&lt;float&gt;</c> is not safe for that, and a torn read showed up as the preview
    /// silently dying mid-dictation.
    /// </summary>
    private readonly Lock _bufferLock = new();

    /// <summary>Transcriptions in flight. Non-zero is the Transcribing state.</summary>
    private int _transcribing;

    /// <summary>The tail of the transcription queue; each new one waits for it.</summary>
    private Task _finishChain = Task.CompletedTask;

    /// <summary>The most recent capture loop, awaited before the next one opens the device.</summary>
    private Task? _lastCaptureLoop;

    /// <summary>Current state.</summary>
    public DictationState State =>
        _current is not null ? DictationState.Recording
        : Volatile.Read(ref _transcribing) > 0 ? DictationState.Transcribing
        : DictationState.Idle;

    /// <summary>True once the microphone has delivered audio for this recording.</summary>
    public bool IsCaptureReady => _current?.CaptureReady == true;

    /// <summary>Most recent input level, 0…1. Drives the meter.</summary>
    public float Level { get; private set; }

    /// <summary>Whether the push-to-talk hook is installed and listening.</summary>
    public bool IsHotkeyArmed { get; private set; }

    /// <summary>The most recent fault, or null once a dictation has since succeeded.</summary>
    public string? LastFault { get; private set; }

    /// <summary>Whether text should be typed into the focused app after a dictation.</summary>
    public bool InjectText { get; set; } = true;

    /// <summary>Terminal spoken command that presses Enter; blank disables it.</summary>
    public string SendWord { get; set; } = "blob";

    /// <summary>Comma-separated alternative words recognised only at the end of dictation.</summary>
    public string SendWordAliases { get; set; } = string.Empty;

    /// <summary>Terminal send phrase; spoken alone it sends existing text. Blank disables it.</summary>
    public string SendOnlyPhrase { get; set; } = "send it";

    /// <summary>Mishearings accepted as <see cref="SendOnlyPhrase"/> when spoken alone.</summary>
    public string SendOnlyAliases { get; set; } = SpokenSendCommand.DefaultSendOnlyAliases;

    /// <summary>Copies the final transcript before delivery, even when automatic typing is off.</summary>
    public Func<string, Task>? CopyTranscriptAsync { get; set; }

    /// <summary>What happens to a full stop at the very end.</summary>
    public TrailingFullStop FullStops { get; set; } = TrailingFullStop.DropAfterSingleSentence;

    /// <summary>Shorthand for <see cref="FullStops"/> being the single-sentence rule.</summary>
    public bool DropSingleSentenceFullStop
    {
        get => FullStops == TrailingFullStop.DropAfterSingleSentence;
        set => FullStops = value ? TrailingFullStop.DropAfterSingleSentence : TrailingFullStop.Keep;
    }

    /// <summary>How the key works. See <see cref="ActivationMode"/>. Hold by default; settings choose Automatic.</summary>
    public ActivationMode Mode { get; set; } = ActivationMode.Hold;

    /// <summary>Shorthand for <see cref="Mode"/> being <see cref="ActivationMode.Tap"/>.</summary>
    public bool TapToToggle
    {
        get => Mode == ActivationMode.Tap;
        set => Mode = value ? ActivationMode.Tap : ActivationMode.Hold;
    }

    /// <summary>
    /// In <see cref="ActivationMode.Automatic"/>, a press shorter than this is a tap and
    /// leaves the recording running; anything longer was a hold and stops on release.
    /// </summary>
    public static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(400);

    /// <summary>Whether "new line", "full stop", "scratch that" and so on are applied locally.</summary>
    public bool SpokenCommands { get; set; } = true;

    /// <summary>Whether "um", "er" and friends are removed locally.</summary>
    public bool RemoveFillers { get; set; } = true;

    /// <summary>House style: the comma before "and" is removed from every result.</summary>
    public bool NoCommaBeforeAnd { get; set; }

    /// <summary>American spellings from the speech model are written the British way.</summary>
    public bool BritishSpelling { get; set; } = true;

    /// <summary>
    /// Clean a long dictation as a whole after the key-up rather than piece by piece while
    /// it is spoken. Slower, and the model sees every sentence with its neighbours.
    /// </summary>
    public bool ReviewWholeDictation { get; set; }

    /// <summary>Whether the next dictation into the same field is joined to the last with a space or a full stop.</summary>
    public bool JoinDictations { get; set; } = true;

    /// <summary>
    /// Whether a dictation started part way through a sentence loses the capital on its
    /// first word. The text before the caret is read at key-down, while the user is still
    /// talking, and only collected at delivery.
    /// </summary>
    public bool MatchCaseToCaret { get; set; } = true;

    /// <summary>
    /// How long the AI tier is given after the key-up, over everything it still has to do,
    /// before the local text is typed instead. Each call has its own transport timeout;
    /// this bounds the total, so a stalled network never holds a dictation for twice it.
    /// </summary>
    public static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(4);

    /// <summary>The last text typed, for <see cref="ContinuationRule"/>.</summary>
    private TypedDelivery? _lastDelivery;

    /// <summary>
    /// Turns other applications' playback down while recording. Null where the platform
    /// offers none.
    /// </summary>
    public IAudioDucker? Ducker { get; set; }

    /// <summary>Whether <see cref="Ducker"/> is used. Read at the start of each recording.</summary>
    public bool DuckAudio
    {
        get { lock (_audioLock) return _duckAudio; }
        set
        {
            lock (_audioLock)
            {
                _duckAudio = value;
                if (!value) RestoreAudio();
            }
        }
    }

    private readonly object _audioLock = new();
    private bool _duckAudio = true;

    private bool _ducked;

    /// <summary>
    /// The running transcript of the current recording, refreshed every
    /// <see cref="PreviewInterval"/> once a second of audio exists. Empty when idle.
    /// </summary>
    public string Preview { get; private set; } = string.Empty;

    /// <summary>Raised when <see cref="Preview"/> changes. Engine thread.</summary>
    public event EventHandler? PreviewChanged;

    /// <summary>How often the preview is re-decoded while recording.</summary>
    public static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(700);

    /// <summary>Preview needs at least this much audio to be worth decoding.</summary>
    public static readonly TimeSpan PreviewMinimum = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How much of the newest audio the preview decodes. Everything before it has already
    /// been shown and does not change, so re-decoding it every tick only costs time and
    /// memory — a 60-second pass is about 2.5 GB, and past the encoder's ceiling it throws.
    /// </summary>
    public static readonly TimeSpan PreviewWindow = TimeSpan.FromSeconds(25);

    /// <summary>How long shutdown waits for a dictation in flight before giving up on it.</summary>
    public static readonly TimeSpan DisposeGrace = TimeSpan.FromSeconds(5);

    private DateTimeOffset _pressedAt;

    /// <summary>The generative clean-up, or null when none is configured.</summary>
    public ITranscriptCleaner? Cleaner { get; set; }

    /// <summary>
    /// The cloud speech-to-text whose words are typed, or null to type the local model's.
    /// It hears the audio while the key is held; the local model still draws the preview
    /// and stands in whenever the cloud fails or is late.
    /// </summary>
    public IStreamingTranscriber? CloudTranscriber { get; set; }

    /// <summary>
    /// How long after the key-up the cloud's final transcript is waited for before the
    /// local one is used. It normally lands in 0.3-0.7 s; the clean-up has its own budget.
    /// </summary>
    public TimeSpan CloudBudget { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The decision model, or null. Runs in the shadow for now: on every dictation it is
    /// asked the questions the rules already answered, its answers are logged beside the
    /// rules', and nothing waits for it or acts on it. A week of that log decides what it
    /// gets to decide.
    /// </summary>
    public IDecisionModel? Decisions { get; set; }

    /// <summary>Whether <see cref="Cleaner"/> is used. Off means the raw path, always.</summary>
    public bool AiCleanup { get; set; }

    /// <summary>
    /// Whether the key does anything. Off leaves the hook installed but ignores it, so
    /// switching back on is instant and nothing is re-registered.
    /// </summary>
    public bool IsEnabled
    {
        get { lock (_audioLock) return _isEnabled; }
        set
        {
            lock (_audioLock)
            {
                _isEnabled = value;
                if (!value) RestoreAudio();
            }
            if (!value) Cancel();
        }
    }

    private bool _isEnabled = true;

    /// <summary>The push-to-talk key, as a virtual-key code. Applies to the next press.</summary>
    public int HotkeyVirtualKey
    {
        get => _hotkey.VirtualKey;
        set { _hotkey.VirtualKey = value; Log.Info($"hotkey changed to 0x{value:X2}"); }
    }

    /// <summary>Raised once after <see cref="BeginCapture"/> with what the user pressed. Hook thread.</summary>
    public event EventHandler<(int VirtualKey, int Modifiers)>? Captured
    {
        add => _hotkey.Captured += value;
        remove => _hotkey.Captured -= value;
    }

    /// <summary>Raised when a capture ends because the user pressed Escape. Hook thread.</summary>
    public event EventHandler? CaptureCancelled
    {
        add => _hotkey.CaptureCancelled += value;
        remove => _hotkey.CaptureCancelled -= value;
    }

    /// <summary>Records the next chord instead of acting on it.</summary>
    public void BeginCapture() => _hotkey.BeginCapture();

    /// <summary>Abandons a capture.</summary>
    public void CancelCapture() => _hotkey.CancelCapture();

    /// <summary>Modifiers required with the key, as <see cref="HotkeyModifiers"/> flags.</summary>
    public int HotkeyModifiers
    {
        get => _hotkey.Modifiers;
        set { _hotkey.Modifiers = value; Log.Info($"hotkey modifiers changed to {(Abstractions.HotkeyModifiers)value}"); }
    }

    /// <summary>Raised when a dictation completes and produced text.</summary>
    public event EventHandler<DictationResult>? Completed;

    /// <summary>Raised only after the explicit send command successfully presses Enter.</summary>
    public event EventHandler? Sent;

    /// <summary>Raised whenever <see cref="State"/> or <see cref="Level"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with a human-readable message when something went wrong.</summary>
    public event EventHandler<string>? Faulted;

    /// <summary>
    /// Raised when a recording ended and nothing was typed, with a short reason such as
    /// "Nothing heard". Not a fault: the user simply did not say anything usable, and an
    /// overlay that just vanishes reads as the app having failed.
    /// </summary>
    public event EventHandler<string>? Dropped;

    /// <summary>Wires the engine to its platform implementations.</summary>
    /// <param name="capture">Microphone source.</param>
    /// <param name="hotkey">Push-to-talk source.</param>
    /// <param name="transcriber">Speech engine.</param>
    /// <param name="injector">Where finished text goes.</param>
    /// <param name="dictionary">
    /// Read fresh on every utterance rather than captured once, so edits take effect without
    /// a restart.
    /// </param>
    /// <param name="clock">Time source; defaults to the system clock.</param>
    public DictationEngine(
        IAudioCapture capture,
        IHotkeySource hotkey,
        ITranscriber transcriber,
        ITextInjector injector,
        Func<IReadOnlyList<DictionaryEntry>> dictionary,
        IClock? clock = null)
    {
        _capture = capture;
        _hotkey = hotkey;
        _transcriber = transcriber;
        _injector = injector;
        _dictionary = dictionary;
        _clock = clock ?? SystemClock.Instance;

        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
        _hotkey.CancelPressed += OnCancelPressed;
    }

    /// <summary>Arms the hotkey.</summary>
    /// <returns>False if the hook could not be installed.</returns>
    public bool Start()
    {
        IsHotkeyArmed = _hotkey.Start();
        Log.Info(IsHotkeyArmed ? "hotkey armed" : "hotkey could NOT be installed");

        if (!IsHotkeyArmed) Fault("The push-to-talk key could not be hooked. Try restarting Acapella.");
        return IsHotkeyArmed;
    }

    /// <summary>
    /// Loads the speech model ahead of the first utterance.
    /// </summary>
    /// <remarks>
    /// The model takes a couple of seconds to load, and paying that on the first dictation
    /// reads as the app being broken. Called at startup, and again after a download.
    /// </remarks>
    /// <returns>True if a model is loaded.</returns>
    public async Task<bool> PreloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var started = _clock.Now;
            var ready = await _transcriber.LoadAsync(cancellationToken).ConfigureAwait(false);
            Log.Info(ready
                ? $"model loaded in {(_clock.Now - started).TotalMilliseconds:0} ms"
                : "model not available");
            return ready;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error("model load failed", e);
            Fault($"The speech model failed to load: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts or stops recording from a button rather than the hotkey.
    /// </summary>
    /// <remarks>
    /// Routed through the same state machine as the hotkey, deliberately. Two independent
    /// paths into recording would eventually disagree about whether it is running. A press
    /// while the previous dictation is still transcribing starts a new recording.
    /// </remarks>
    public void TogglePushToTalk()
    {
        if (_current is null) _ = BeginAsync();
        else _ = EndAsync();
    }

    /// <summary>Discards the recording in progress, typing nothing.</summary>
    public void Cancel()
    {
        if (_current is { } session) _ = AbandonAsync(session);
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        if (!IsEnabled) return;
        _pressedAt = _clock.Now;

        switch (Mode)
        {
            case ActivationMode.Hold:
                _ = BeginAsync();
                break;
            case ActivationMode.Tap:
            case ActivationMode.Automatic:
                TogglePushToTalk();
                break;
        }
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        if (!IsEnabled && _current is null) return;

        switch (Mode)
        {
            case ActivationMode.Hold:
                _ = EndAsync();
                break;
            case ActivationMode.Automatic:
                // A quick tap leaves it running; a hold was push-to-talk and ends here.
                if (_clock.Now - _pressedAt >= TapThreshold) _ = EndAsync();
                break;
        }
    }

    private void OnCancelPressed(object? sender, EventArgs e) => Cancel();

    private async Task BeginAsync()
    {
        Session session;
        Task? previousLoop;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsEnabled || _current is not null) return;

            // Owned by the engine until EndAsync or AbandonAsync hands it on; disposed
            // once its transcription (or its cancellation) has finished with the token.
            session = new Session { StartedAt = _clock.Now };

            // First thing at key-down, on a pool thread, so an app that answers slowly
            // cannot hold the recording. Nothing waits for it before delivery.
            if ((MatchCaseToCaret || JoinDictations) && InjectText)
            {
                var injector = _injector;
                var stop = session.Stop.Token;
                session.CaretContext = Task.Run(() => injector.ReadTextBeforeCaretAsync(CaretCase.ContextLength, stop), CancellationToken.None);
            }

            // Opened now so the socket and setup, about half a second, overlap the first words.
            session.Cloud = StartCloud();

            lock (_bufferLock) _current = session;
            SetPreview(string.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
            session.Preview = PreviewLoopAsync(session);

            // The previous recording's loop may still be closing its capture enumerator;
            // the device cannot be opened twice, so this one waits for it first.
            previousLoop = _lastCaptureLoop;
            session.CaptureLoop = CaptureLoopAsync(session, previousLoop);
            _lastCaptureLoop = session.CaptureLoop;
        }
        finally
        {
            _gate.Release();
        }

        // The user is about to talk for a while; use that time to open the clean-up connection.
        if (AiCleanup && Cleaner is { } warm) _ = WarmUpCleanerAsync(warm);

        await session.CaptureLoop.ConfigureAwait(false);
    }

    private bool IsCurrent(Session session) => ReferenceEquals(_current, session);

    /// <summary>Whether two transcripts have the same words, ignoring case and punctuation.</summary>
    private static bool SameWords(string a, string b)
    {
        static string Words(string s) => string.Join(' ', new string([.. s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '\'' ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Words(a) == Words(b);
    }

    private IStreamingTranscription? StartCloud()
    {
        if (CloudTranscriber is not { } cloud) return null;
        try
        {
            return cloud.Start(DictionaryCorrector.BiasPhrases(_dictionary()));
        }
        catch (Exception e)
        {
            Log.Warn($"cloud transcription could not start: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Hands the cloud everything recorded since the last call, never the pre-roll that other
    /// apps' playback was on. Called with <see cref="_bufferLock"/> held.
    /// </summary>
    private static void FeedCloud(Session session)
    {
        if (session.Cloud is not { } cloud) return;
        var start = Math.Max(session.CloudSent, session.SkipSamples);
        if (session.Buffer.Count <= start) return;
        cloud.Append(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(session.Buffer)[start..]);
        session.CloudSent = session.Buffer.Count;
    }

    /// <summary>Ends the cloud's dictation and waits, within <see cref="CloudBudget"/>, for its words.</summary>
    private static async Task<string?> FinishCloudAsync(IStreamingTranscription cloud, string name, TimeSpan within)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var budget = new CancellationTokenSource(within);
        try
        {
            var text = await cloud.FinishAsync(budget.Token).ConfigureAwait(false);
            if (text is null) Log.Warn($"cloud transcription by {name} failed after {clock.ElapsedMilliseconds} ms ({cloud.LastError ?? "no reason given"}); typing the local model's words");
            else Log.Info($"cloud transcription by {name}: final {clock.ElapsedMilliseconds} ms after the key-up, {text.Length} chars");
            return text;
        }
        catch (Exception e)
        {
            Log.Warn($"cloud transcription by {name} failed: {e.Message}");
            return null;
        }
    }

    private async Task CaptureLoopAsync(Session session, Task? previousLoop)
    {
        if (previousLoop is { IsCompleted: false })
        {
            try { await previousLoop.ConfigureAwait(false); }
            catch (Exception) { /* reported when it happened */ }
        }

        var token = session.Stop.Token;
        try
        {
            await foreach (var chunk in _capture.CaptureAsync(token).ConfigureAwait(false))
            {
                // Stop consuming the moment this recording ends. Cancellation is
                // cooperative, so chunks already queued still arrive after EndAsync has
                // moved on — and without this guard one of them sets Level back to a
                // reading that has already been zeroed.
                lock (_bufferLock)
                {
                    if (!IsCurrent(session)) break;
                    // Copied, not referenced: capture implementations are entitled to reuse
                    // their buffer the moment this returns.
                    session.Buffer.AddRange(chunk.Samples.Span);
                }
                Level = chunk.Rms();
                if (!session.Heard && Level >= SilenceFloor)
                {
                    // How long after the key press the device produced sound rather than
                    // zeros. With the warm capture this should read zero or close to it.
                    session.Heard = true;
                    Log.Info($"first audible audio after {(_clock.Now - session.StartedAt).TotalMilliseconds:0} ms");
                }
                if (!session.CaptureReady)
                {
                    session.CaptureReady = true;
                    // Read now: the warm capture sets it as the session opens, and the
                    // preview needs it before its first commit.
                    session.PreRoll = _capture.PreRollDelivered;
                    Log.Info($"microphone delivering audio after {(_clock.Now - session.StartedAt).TotalMilliseconds:0} ms");
                    // The start cue is raised before the duck: enumerating and muting the
                    // other apps' sessions takes 12 ms typically and 435 ms at worst, and
                    // the cue waited behind it.
                    Changed?.Invoke(this, EventArgs.Empty);
                    lock (_audioLock)
                    {
                        if (!IsCurrent(session)) break;
                        // Capture is already running and queueing audio while Windows enumerates
                        // and mutes sessions. Never put that work ahead of opening the microphone.
                        if (_isEnabled && _duckAudio && !_ducked && Ducker is { } ducker)
                        {
                            _ducked = true;
                            var (peak, source) = ducker.Duck();
                            session.OtherAudioWasPlaying = peak >= LoudPlaybackPeak;
                            if (session.OtherAudioWasPlaying)
                            {
                                lock (_bufferLock) session.SkipSamples = (int)(session.PreRoll.TotalSeconds * AudioChunk.SampleRate);
                            }
                            Log.Info(peak > 0
                                ? $"other audio ducked (peak {peak:0.00} from {source ?? "unknown"}{(session.OtherAudioWasPlaying ? ", loud enough to drop the pre-roll" : string.Empty)})"
                                : "other audio ducked");
                        }
                    }
                }
                // After the duck, which decides whether the pre-roll is the user's or another app's.
                lock (_bufferLock)
                {
                    if (IsCurrent(session)) FeedCloud(session);
                }
                Changed?.Invoke(this, EventArgs.Empty);
            }

            // The stream ended on its own while the key was still down: a device that
            // stopped cleanly, or a test fake that ran out of audio. What was captured is
            // kept and transcribed on release; the user should not lose a sentence to a
            // hiccup they cannot see.
            if (IsCurrent(session) && !token.IsCancellationRequested)
            {
                Log.Warn("microphone stream ended before the key was released; keeping what was captured");
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: the key was released.
        }
        catch (Exception e)
        {
            // The device vanished, the assembly did not load, the format was refused. The
            // recording that was in flight is over; say so and get back to Idle.
            Log.Error("audio capture failed", e);
            Fault($"The microphone could not be opened: {e.Message}");
            await AbandonAsync(session).ConfigureAwait(false);
        }
        finally
        {
            // Authoritative for this recording: once its loop has genuinely finished nothing
            // can raise the level afterwards and leave the meter stuck. A newer recording
            // owns the meter now and is left alone.
            if (_current is null || IsCurrent(session))
            {
                Level = 0;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static async Task WarmUpCleanerAsync(ITranscriptCleaner cleaner)
    {
        try { await cleaner.WarmUpAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception e) { Log.Warn($"clean-up warm-up failed: {e.Message}"); }
    }

    private async Task AbandonAsync(Session session)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsCurrent(session)) return;

            // Everything a watcher can observe is put right BEFORE the state flips to
            // Idle: audio restored, level zeroed, preview cleared. Clearing the session
            // first left a window in which the engine read Idle with the music still muted.
            RestoreAudio();
            Level = 0;
            SetPreview(string.Empty);
            lock (_bufferLock) _current = null;
            await session.Stop.CancelAsync().ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
            Log.Info("recording cancelled");
        }
        finally
        {
            _gate.Release();
        }

        // The preview may still be inside the model; let it finish before the token source
        // goes away, so the next recording never shares the recogniser with a stale decode.
        if (session.Preview is { } preview) await preview.ConfigureAwait(false);
        session.Dispose();
    }

    /// <summary>
    /// Re-decodes the audio so far while the key is held, so words appear as they are
    /// spoken rather than all at once on release.
    /// </summary>
    /// <remarks>
    /// The offline model decodes many times faster than real time, so a pass over the live
    /// window every 700 ms costs a fraction of a second. A tick that finds the previous one
    /// still running is skipped, and the final pass waits for the last tick so the model is
    /// never asked to decode two streams at once.
    /// </remarks>
    private async Task PreviewLoopAsync(Session session)
    {
        var cancellationToken = session.Stop.Token;

        // Text decoded from audio that has since scrolled out of the window. Fixed once
        // committed, so the running transcript reads as one piece rather than flickering.
        var committed = string.Empty;
        var committedSamples = 0;
        var windowSamples = (int)(PreviewWindow.TotalSeconds * AudioChunk.SampleRate);
        var minimumSamples = (int)(PreviewMinimum.TotalSeconds * AudioChunk.SampleRate);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PreviewInterval, cancellationToken).ConfigureAwait(false);

                // A key-up the hook never saw — delivered to an elevated window or the
                // secure desktop — would otherwise leave the recording running until the
                // next press. Only in Hold mode: a tap in Automatic mode is meant to keep
                // recording with the key up.
                if (Mode == ActivationMode.Hold && IsCurrent(session) && !_hotkey.IsTriggerHeld)
                {
                    Log.Warn("the key is up but no release was seen; ending the recording");
                    _ = EndAsync();
                    return;
                }

                if (!_transcriber.IsReady) continue;

                float[] snapshot;
                float[]? toCommit = null;
                lock (_bufferLock)
                {
                    if (!IsCurrent(session)) return;
                    // Other apps' playback at the start of the buffer is never decoded,
                    // by the preview or by the final pass.
                    if (committedSamples < session.SkipSamples) committedSamples = session.SkipSamples;
                    if (session.Buffer.Count - committedSamples < minimumSamples) continue;

                    var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(session.Buffer);

                    // Once the live window outgrows its limit, decode its older half once
                    // and freeze that text; from then on only the newest audio is
                    // re-decoded. The cut lands on the quietest moment so it falls between
                    // words rather than through one.
                    if (session.Buffer.Count - committedSamples > windowSamples)
                    {
                        var idealCut = session.Buffer.Count - (windowSamples / 2);
                        var cut = AudioSegmenter.QuietestPoint(span, idealCut - (AudioSegmenter.SilenceSearchSeconds * AudioChunk.SampleRate), idealCut);
                        if (cut > committedSamples)
                        {
                            toCommit = span[committedSamples..cut].ToArray();
                            committedSamples = cut;
                        }
                    }

                    snapshot = span[committedSamples..].ToArray();
                }

                if (toCommit is not null)
                {
                    var frozen = (await _transcriber.TranscribeAsync(toCommit, [], cancellationToken).ConfigureAwait(false)).Trim();
                    if (frozen.Length > 0) committed = committed.Length == 0 ? frozen : committed + " " + frozen;

                    // Recorded for the final pass, which then decodes only what follows.
                    // Clean-up of the piece starts now, while the user is still talking, so
                    // the wait after key-up covers the last window of speech only.
                    lock (_bufferLock)
                    {
                        var previous = session.Committed.Count > 0 ? session.Committed[^1].Clean : null;
                        // With "review the whole dictation" on, nothing is cleaned early:
                        // the one call after the key-up sees every sentence with its
                        // neighbours, at the cost of waiting for it.
                        // With cloud transcription the local words are only the fallback, so
                        // cleaning them early would be wasted; the cloud's are cleaned whole.
                        var cleaner = AiCleanup && !ReviewWholeDictation && session.Cloud is null ? Cleaner : null;
                        // A silent stretch is a completed, empty piece, not a missing
                        // one: null here would mean "the AI tier could not" and quietly
                        // send the whole dictation round again after the key-up.
                        var job = cleaner is null ? null
                            : frozen.Length == 0 ? EmptySegmentAsync(previous)
                            : CleanSegmentAsync(frozen, previous, cleaner);
                        session.Committed.Add(new CommittedPiece(committedSamples, frozen, job));
                        session.CommittedSamples = committedSamples;
                    }
                }

                var text = await _transcriber.TranscribeAsync(snapshot, [], cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested && IsCurrent(session))
                {
                    var tail = text.Trim();
                    SetPreview(committed.Length == 0 ? tail : tail.Length == 0 ? committed : committed + " " + tail);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The key was released.
        }
        catch (Exception e)
        {
            // Preview is a nicety. It must never take the real transcription down with it.
            Log.Warn($"preview stopped: {e.Message}");
        }
    }

    private void SetPreview(string text)
    {
        if (text == Preview) return;
        Preview = text;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs the dictionary, the rules and the AI tier over one committed piece, with the
    /// pieces before it as context. Null means the AI tier could not clean it, in which
    /// case the final pass cleans the whole dictation in one go, as it always did.
    /// </summary>
    private async Task<SegmentClean?> CleanSegmentAsync(string raw, Task<SegmentClean?>? previous, ITranscriptCleaner cleaner)
    {
        try
        {
            var soFar = string.Empty;
            if (previous is not null)
            {
                var before = await previous.ConfigureAwait(false);
                if (before is null) return null;
                soFar = before.CleanedSoFar;
            }

            var (text, applied) = new DictionaryCorrector(_dictionary()).Apply(raw);
            var local = SpokenFormatting.Apply(text, SpokenCommands, RemoveFillers);

            string cleaned;
            if (string.IsNullOrWhiteSpace(local)) cleaned = string.Empty;
            else if (!CleanupGuard.IsWorthCleaning(local)) cleaned = local;
            else
            {
                // The piece was cut at a pause, so the full stop the speech model closed it
                // with says nothing; the cleaner sees the words without it and decides.
                var forCleaner = PieceText.WithoutArtificialStop(ForCleaner(text));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var reply = await cleaner.CleanPieceAsync(forCleaner, soFar.Length > 0 ? soFar : null, CancellationToken.None).ConfigureAwait(false);
                if (reply is null || !CleanupGuard.IsPlausible(forCleaner, PieceText.WithoutNewSentenceMark(reply)))
                {
                    Log.Warn($"AI clean-up of a {local.Length}-char piece during recording failed after {clock.ElapsedMilliseconds} ms ({(reply is null ? cleaner.LastError ?? "no reason given" : "rewrote rather than tidied")})");
                    return null;
                }
                Log.Info($"AI clean-up of a {local.Length}-char piece during recording: {clock.ElapsedMilliseconds} ms");
                cleaned = reply;
            }

            var joined = PieceText.JoinCleaned(soFar, cleaned);
            ShadowDecisions.AboutJoin(this, soFar, cleaned, joined);
            return new SegmentClean(local, cleaned, applied, joined);
        }
        catch (Exception e)
        {
            Log.Warn($"clean-up during recording failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Runs one clean-up call under the key-up budget. A cleaner that honours the token by
    /// throwing rather than returning null is treated the same: nothing came back.
    /// </summary>
    private static async Task<string?> CleanWithinBudgetAsync(Func<Task<string?>> call, CancellationToken budget)
    {
        try { return await call().ConfigureAwait(false); }
        catch (OperationCanceledException) when (budget.IsCancellationRequested) { return null; }
    }

    /// <summary>A frozen piece that decoded to nothing: the earlier text carries on unchanged.</summary>
    private static async Task<SegmentClean?> EmptySegmentAsync(Task<SegmentClean?>? previous)
    {
        var before = previous is null ? null : await previous.ConfigureAwait(false);
        if (previous is not null && before is null) return null;
        return new SegmentClean(string.Empty, string.Empty, [], before?.CleanedSoFar ?? string.Empty);
    }

    /// <summary>
    /// What the AI tier is shown: the dictionary's corrections and the filler rule applied,
    /// the spoken commands left as words. "Period", "full stop" and "new line" are then
    /// judged in context by the one component that can, and the local rules' reading is
    /// kept only as the fallback typed when the tier does not answer.
    /// </summary>
    private string ForCleaner(string dictionaryText) => SpokenFormatting.Apply(dictionaryText, commands: false, RemoveFillers);

    private static string JoinText(string first, string second) =>
        first.Length == 0 ? second : second.Length == 0 ? first : first + " " + second;

    private async Task EndAsync()
    {
        var deliveryClock = System.Diagnostics.Stopwatch.StartNew();
        Task work;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_current is not { } session) return;

            // Counted as transcribing BEFORE the session is cleared, so State goes straight
            // from Recording to Transcribing and never reads Idle in between — a watcher
            // that saw Idle here would believe the dictation was over before it began.
            Interlocked.Increment(ref _transcribing);
            RestoreAudio();
            Level = 0;
            lock (_bufferLock) _current = null;
            await session.Stop.CancelAsync().ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);

            // Queued behind any transcription still running, so two dictations spoken back
            // to back are typed in the order they were spoken.
            work = FinishAsync(session, _finishChain, deliveryClock);
            _finishChain = work;
        }
        finally
        {
            _gate.Release();
        }

        await work.ConfigureAwait(false);
    }

    private async Task FinishAsync(Session session, Task previous, System.Diagnostics.Stopwatch deliveryClock)
    {
        try
        {
            if (session.Preview is { } preview) await preview.ConfigureAwait(false);
            await previous.ConfigureAwait(false);
            Log.Info($"stop-to-final processing: {deliveryClock.ElapsedMilliseconds} ms (capture stop and preview wait)");
            await ProcessAsync(session).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error("dictation failed", e);
            Fault($"Transcription failed: {e.Message}");
        }
        finally
        {
            Log.Info($"stop-to-complete: {deliveryClock.ElapsedMilliseconds} ms");
            session.Dispose();
            Interlocked.Decrement(ref _transcribing);
            // A newer recording owns the preview now; only an idle engine clears it.
            if (_current is null) SetPreview(string.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Recordings shorter than this are dropped without transcribing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Observed on real hardware: a brief tap of the key — often a Shift pressed for a
    /// capital letter — yields 30 to 400 ms of room tone, and Parakeet hallucinates
    /// "Mm-hmm." onto it, which then gets typed. No word fits in less than this.
    /// </para>
    /// <para>
    /// Judged after subtracting <see cref="IAudioCapture.PreRollDelivered"/>: the warm
    /// capture prepends audio from before the key press, so even a tap arrives with more
    /// than this much audio.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan MinimumUtterance = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Recordings whose overall level is below this are dropped as silence, whatever their
    /// length. Speech into any working microphone measures well above it.
    /// </summary>
    public const float SilenceFloor = 0.002f;

    /// <summary>
    /// A recording whose loudest sample is below this carried no room tone at all: a
    /// muted device, not a quiet speaker. Room tone through any live microphone peaks
    /// well above it.
    /// </summary>
    public const float DigitalSilencePeak = 0.0005f;

    /// <summary>
    /// Playback peaking at or above this when the key was pressed is loud enough to have
    /// reached the microphone, so the pre-roll recorded under it is discarded.
    /// </summary>
    /// <remarks>
    /// Any "active session with signal" test was wrong: on a studio machine a mixer or a
    /// loopback keeps a session active at a few percent all day, and on 2026-09-14 that
    /// dropped the pre-roll on every single dictation — the first-word loss came straight
    /// back. Speech and video at listening volume peak well above this; a resting mixer
    /// does not. The level and source are logged on every recording for calibration.
    /// </remarks>
    public const float LoudPlaybackPeak = 0.2f;

    private async Task ProcessAsync(Session session)
    {
        var samples = session.Buffer;

        // The end of the dictation goes to the cloud before anything else, so its final
        // transcript is on its way while the local model decodes.
        Task<string?>? cloudFinal = null;
        if (session.Cloud is { } cloud)
        {
            lock (_bufferLock) FeedCloud(session);
            cloudFinal = FinishCloudAsync(cloud, CloudTranscriber?.Name ?? "cloud", CloudBudget);
        }

        // The pre-roll was captured while other playback was still at full volume, so its
        // words are the video's, not the user's. It is the first thing the warm capture
        // delivered, so it is the start of the buffer. The preview knew to skip it from
        // the first chunk, so anything it froze is kept; only a short dictation, where
        // nothing was frozen, drops the samples here.
        if (session.OtherAudioWasPlaying && session.PreRoll > TimeSpan.Zero)
        {
            var preRollSamples = Math.Min(samples.Count, (int)(session.PreRoll.TotalSeconds * AudioChunk.SampleRate));
            int kept;
            lock (_bufferLock)
            {
                kept = session.Committed.Count;
                if (kept == 0)
                {
                    samples.RemoveRange(0, preRollSamples);
                    session.SkipSamples = 0;
                }
            }
            Log.Info($"dropped {session.PreRoll.TotalSeconds:0.0}s of pre-roll: other audio was playing loudly"
                   + (kept > 0 ? $"; {kept} piece(s) frozen during recording kept" : string.Empty));
            session.PreRoll = TimeSpan.Zero;
        }

        if (samples.Count == 0) return;

        var seconds = (double)samples.Count / AudioChunk.SampleRate;
        var utterance = seconds - session.PreRoll.TotalSeconds;
        if (utterance < MinimumUtterance.TotalSeconds)
        {
            Log.Info($"ignored a {utterance * 1000:0} ms tap of the key");
            return;
        }

        var audio = new ReadOnlyMemory<float>(samples.ToArray());
        if (new AudioChunk(audio).Rms() < SilenceFloor)
        {
            // Only a recording that was itself silent can mean the microphone is blocked.
            // The flag is a property of the device stream, and a stale one — a headset
            // muted for a call, unmuted since — must never throw away audible speech.
            if (_capture.LooksLikeBlockedMicrophone)
            {
                Log.Warn("capture delivered only digital silence — microphone looks blocked");
                Fault(BlockedMicrophoneMessage);
                return;
            }

            // The stream was open and delivering, so this is the microphone itself giving
            // nothing: on Dave's desk a muted Wave XLR during a call, in bursts of two or
            // three attempts that recover on their own. Say so, rather than "nothing heard".
            var peak = 0f;
            foreach (var sample in audio.Span) peak = Math.Max(peak, Math.Abs(sample));
            Log.Warn($"ignored {seconds:0.0}s of silence (peak {peak:0.0000}; the microphone is delivering audio but no sound, muted?)");
            Dropped?.Invoke(this, peak < DigitalSilencePeak ? "Nothing heard. Is the mic muted?" : "Nothing heard");
            return;
        }

        // Found on the first real-hardware run: nothing ever loaded the model, so with the
        // files on disk every transcript still came back empty.
        if (!_transcriber.IsReady && !await _transcriber.LoadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            Log.Warn("no model to transcribe with");
            Fault(ModelMissingMessage);
            return;
        }

        // Measured from key release, because that is the wait the user actually feels — and
        // it is the only figure on which a streaming and a batch engine compare honestly.
        var releasedAt = _clock.Now;

        var entries = _dictionary();
        var bias = DictionaryCorrector.BiasPhrases(entries);

        // Whatever the preview loop froze is final; only the audio after it is decoded now.
        // On a two-minute dictation that turns three seconds of decoding into a fraction of one.
        List<CommittedPiece> committed;
        int committedSamples;
        lock (_bufferLock)
        {
            committed = [.. session.Committed];
            committedSamples = session.CommittedSamples;
        }
        var pieces = AudioSegmenter.Split(audio[Math.Max(committedSamples, session.SkipSamples)..]);
        var tailTranscripts = new List<string>(pieces.Count);

        foreach (var piece in pieces)
        {
            var text = await _transcriber
                .TranscribeAsync(piece, bias, CancellationToken.None)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(text)) tailTranscripts.Add(text.Trim());
        }

        var tailRaw = string.Join(' ', tailTranscripts);
        var raw = JoinText(string.Join(' ', committed.Select(c => c.Raw)), tailRaw);
        Log.Info($"transcribed {audio.Length / (double)AudioChunk.SampleRate:0.0}s of audio "
               + $"in {(_clock.Now - releasedAt).TotalMilliseconds:0} ms: {raw.Length} chars"
               + (committedSamples > 0 ? $" ({committedSamples / (double)AudioChunk.SampleRate:0.0}s reused from the preview)" : string.Empty));

        // The cloud's words win when they arrive in time. An empty answer where the local
        // model heard speech is treated as a failure, not as silence.
        string? transcribedBy = null;
        string? localRaw = null;
        if (cloudFinal is not null && await cloudFinal.ConfigureAwait(false) is { } cloudText && !string.IsNullOrWhiteSpace(cloudText))
        {
            localRaw = raw;
            raw = string.Join(' ', cloudText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            transcribedBy = CloudTranscriber?.Name;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            Dropped?.Invoke(this, "Nothing heard");
            return;
        }

        // The dictionary runs first and unconditionally. Biasing only raises the odds of the
        // right word; this is the pass that guarantees it.
        if (SpokenSendCommand.IsStandalone(raw, SendOnlyPhrase, SendOnlyAliases))
        {
            if (InjectText) await SendToFocusedAppAsync($"\"{raw.Trim()}\" spoken alone").ConfigureAwait(false);
            return;
        }
        // Detect before corrections or AI can alter or invent the command.
        var (content, send) = ExtractSendCommand(raw);
        if (send && string.IsNullOrWhiteSpace(content))
        {
            if (InjectText) await SendToFocusedAppAsync($"\"{raw.Trim()}\" spoken alone").ConfigureAwait(false);
            return;
        }
        ShadowDecisions.AboutDictation(this, raw, send);
        var (dictionaryText, applied) = new DictionaryCorrector(entries).Apply(content);

        // Then the rules: spoken commands and fillers, deterministically, so local-only mode
        // is complete on its own and the AI tier has less to do.
        var local = SpokenFormatting.Apply(dictionaryText, SpokenCommands, RemoveFillers);
        if (string.IsNullOrWhiteSpace(local))
        {
            Log.Info("nothing left after rules (a filler, or a command with nothing before it)");
            Dropped?.Invoke(this, "Nothing to type");
            return;
        }

        // The generative tier sits between the rules and the polish: names arrive already
        // corrected, and the full-stop rule still applies to what comes back. If it fails
        // for any reason the local text is used — a dictation is never lost to the cloud
        // being down. Everything it still has to do after the key-up shares one budget.
        string? cleanedBy = null;
        var cleanupFailed = false;
        var candidate = local;
        var path = CleanupPath.Off;
        string? note = null;
        var cleanupClock = System.Diagnostics.Stopwatch.StartNew();
        using var budget = new CancellationTokenSource(CleanupBudget);

        // Pieces frozen during a long recording were cleaned as they were spoken. If every
        // one of them came back, only the tail is cleaned now, with the rest as context.
        // Anything short of that falls through to the whole-dictation pass below.
        if (AiCleanup && Cleaner is { } tailCleaner && !send && transcribedBy is null && committed.Count > 0 && committed.All(c => c.Clean is not null))
        {
            SegmentClean?[]? earlier = null;
            try { earlier = await Task.WhenAll(committed.Select(c => c.Clean!)).WaitAsync(budget.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { note = $"pieces not finished within {CleanupBudget.TotalSeconds:0} s"; }

            if (earlier is not null && earlier.All(e => e is not null))
            {
                var soFar = earlier[^1]!.CleanedSoFar;
                var (tailText, tailApplied) = new DictionaryCorrector(entries).Apply(tailRaw);
                var tailLocal = SpokenFormatting.Apply(tailText, SpokenCommands, RemoveFillers);
                var tailForCleaner = ForCleaner(tailText);
                string? tailCleaned;
                if (string.IsNullOrWhiteSpace(tailLocal)) tailCleaned = string.Empty;
                else if (!CleanupGuard.IsWorthCleaning(tailLocal)) tailCleaned = tailLocal;
                else
                {
                    tailCleaned = await CleanWithinBudgetAsync(() => tailCleaner.CleanAsync(tailForCleaner, soFar, budget.Token), budget.Token).ConfigureAwait(false);
                    if (tailCleaned is null) note = $"tail not cleaned ({(budget.IsCancellationRequested ? $"not within {CleanupBudget.TotalSeconds:0} s" : tailCleaner.LastError ?? "no reason given")})";
                    else if (!CleanupGuard.IsPlausible(tailForCleaner, PieceText.WithoutNewSentenceMark(tailCleaned)))
                    {
                        note = $"tail rewrote rather than tidied ({tailForCleaner.Length} -> {tailCleaned.Length} chars)";
                        tailCleaned = null;
                    }
                }

                if (tailCleaned is not null)
                {
                    local = JoinText(string.Join(' ', earlier.Select(e => e!.Local)), tailLocal);
                    applied = [.. earlier.SelectMany(e => e!.Applied), .. tailApplied];
                    candidate = PieceText.JoinCleaned(soFar, tailCleaned);
                    ShadowDecisions.AboutJoin(this, soFar, tailCleaned, candidate);
                    cleanedBy = tailCleaner.Name;
                    path = CleanupPath.Piecewise;
                    note = $"tail {tailLocal.Length} chars; {committed.Count} piece(s) cleaned during recording";
                }
            }
            else if (earlier is not null) note ??= "a piece cleaned during recording was unusable";

            if (cleanedBy is null) Log.Info($"clean-up during recording did not complete ({note}); cleaning the whole dictation");
        }

        if (cleanedBy is null && AiCleanup && Cleaner is { } cleaner)
        {
            var forCleaner = ForCleaner(dictionaryText);
            if (!CleanupGuard.IsWorthCleaning(local))
            {
                path = CleanupPath.SkippedShort;
                note = $"{local.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length} words";
            }
            else if (budget.IsCancellationRequested)
            {
                path = CleanupPath.LocalFallback;
                cleanupFailed = true;
            }
            else
            {
                // With two readings that differ, the cleaner chooses between them; identical
                // ones, or a local model that heard nothing, leave only the one.
                var alternative = localRaw is null ? null : ForCleaner(new DictionaryCorrector(entries).Apply(localRaw).Text);
                var twoReadings = alternative is { Length: > 0 } && !SameWords(alternative, forCleaner);
                var cleaned = await CleanWithinBudgetAsync(
                    () => twoReadings ? cleaner.CleanTwoReadingsAsync(forCleaner, alternative!, budget.Token) : cleaner.CleanAsync(forCleaner, budget.Token),
                    budget.Token).ConfigureAwait(false);
                if (twoReadings) note = "chose between the cloud and local readings";
                if (cleaned is not null && !CleanupGuard.IsPlausible(forCleaner, cleaned))
                {
                    // The model summarised or padded. Wispr-grade means never doing that to
                    // someone's words; the local result wins, quietly.
                    note = $"rewrote rather than tidied ({forCleaner.Length} -> {cleaned.Length} chars)";
                    path = CleanupPath.LocalFallback;
                    cleanupFailed = true;
                }
                else if (cleaned is not null)
                {
                    candidate = cleaned;
                    cleanedBy = cleaner.Name;
                    path = CleanupPath.Whole;
                }
                else
                {
                    note = budget.IsCancellationRequested ? $"no answer within {CleanupBudget.TotalSeconds:0} s" : cleaner.LastError ?? "no reason given";
                    path = CleanupPath.LocalFallback;
                    cleanupFailed = true;
                    Fault("AI clean-up did not respond, so the local transcript was typed. Check the key and connection in Settings.");
                }
            }
        }

        // One line per dictation says which path ran and why, so a drop in quality can be
        // read from the log rather than guessed at.
        var summary = $"clean-up: {path}{(cleanedBy is null ? string.Empty : $" by {cleanedBy}")} in {cleanupClock.ElapsedMilliseconds} ms; {local.Length} -> {candidate.Length} chars"
                    + (note is null ? string.Empty : $" ({note})");
        if (cleanupFailed) Log.Warn(summary + "; typed the local text"); else Log.Info(summary);

        // Polish runs last so a correction or a clean-up that ends a sentence is treated
        // the same as one the engine produced itself. The spoken send command was taken
        // out of the raw text before any of this, and is never in the candidate.
        if (NoCommaBeforeAnd) candidate = TranscriptPolish.RemoveCommaBeforeAnd(candidate);
        if (BritishSpelling) candidate = BritishSpellings.Apply(candidate);
        var corrected = TranscriptPolish.Apply(candidate, FullStops);
        var stopDropped = candidate.TrimEnd().EndsWith('.') && !corrected.EndsWith('.');

        var result = new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)audio.Length / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: corrected,
            Corrections: applied,
            CleanedBy: cleanedBy,
            RawText: raw,
            CleanupFailed: cleanupFailed,
            Cleanup: path,
            Audio: audio,
            TranscribedBy: transcribedBy,
            LocalRawText: localRaw);

        if (cleanedBy is not null || !AiCleanup) LastFault = null;

        // The clipboard copy comes before the typing, and must: a paste puts the text on
        // the clipboard and presses Ctrl+V, and Chrome and Electron apps read the
        // clipboard a little after the keypress. A copy made straight after the paste
        // replaced the clipboard under them and they pasted nothing, one time in a few
        // (2026-09-18). Whatever happens to the typing, the words are also already
        // somewhere the user can get at them.
        if (CopyTranscriptAsync is { } copy)
        {
            try { await copy(corrected).ConfigureAwait(false); }
            catch (Exception e) { Log.Warn($"Could not copy transcription to clipboard: {e.Message}"); }
        }

        // The history comes after the typing: its list rebuild lives on the UI thread,
        // and a paste that waited behind it took four times as long with the window open.
        var delivered = false;
        if (InjectText)
        {
            var target = _injector.FocusTarget;
            // Joining requires positive evidence that the last dictation is still in the
            // field. Clicking Send can clear a chat composer without changing its focus
            // identity or the keyboard counter, which otherwise produces a stray leading
            // full stop in the newly empty field.
            var beforeCaret = session.CaretContext is { IsCompletedSuccessfully: true } context ? context.Result : null;
            var prefix = JoinDictations ? ContinuationRule.Prefix(_lastDelivery, corrected, target, _hotkey.UserKeyPresses, _clock.Now, beforeCaret) : string.Empty;

            // Joining to the last dictation already decides the first letter; the caret
            // only has a say when there is no join.
            var typed = corrected;
            var caretNote = string.Empty;
            if (prefix.Length == 0 && MatchCaseToCaret) (typed, caretNote) = await MatchCaseAsync(session, corrected).ConfigureAwait(false);

            var insertionClock = System.Diagnostics.Stopwatch.StartNew();
            delivered = await _injector.InjectAsync(prefix + typed, CancellationToken.None).ConfigureAwait(false);
            Log.Info($"text insertion: {insertionClock.ElapsedMilliseconds} ms; accepted={delivered}"
                   + (prefix.Length == 0 ? string.Empty : $"; joined to the last dictation with \"{prefix}\"")
                   + caretNote);
            _lastDelivery = delivered ? new TypedDelivery(typed, stopDropped, target, _hotkey.UserKeyPresses, _clock.Now, send) : null;
        }

        Completed?.Invoke(this, result);

        if (!InjectText) return;

        if (!delivered)
        {
            Log.Warn("text could not be delivered to the focused app");
            Fault("The text could not be typed into the focused app. It is on the clipboard and in the history.");
        }
        else if (send)
        {
            await SendToFocusedAppAsync("send word").ConfigureAwait(false);
        }
    }

    /// <summary>How long delivery will wait for a caret read that has not finished. Normally it is long done.</summary>
    private static readonly TimeSpan CaretGrace = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// <paramref name="text"/> with the capital taken off if the caret was mid-sentence when
    /// recording began, and a log fragment saying what was known.
    /// </summary>
    /// <remarks>
    /// The read started at key-down and the user has since talked for a while, so it is
    /// almost always finished; the small grace only matters for a very short dictation. A
    /// read that has not answered, failed or found nothing leaves the text exactly as it was.
    /// </remarks>
    private static async Task<(string Text, string Note)> MatchCaseAsync(Session session, string text)
    {
        if (session.CaretContext is not { } read) return (text, string.Empty);

        try
        {
            if (!read.IsCompleted) await Task.WhenAny(read, Task.Delay(CaretGrace)).ConfigureAwait(false);
            if (!read.IsCompletedSuccessfully) return (text, "; caret context: not ready");
            if (read.Result is not { } before) return (text, "; caret context: unavailable in this app");

            var typed = CaretCase.Apply(text, before);
            return (typed, CaretCase.IsMidSentence(before)
                ? (ReferenceEquals(typed, text) ? "; caret context: mid-sentence, capital kept" : "; caret context: mid-sentence, capital dropped")
                : "; caret context: sentence start");
        }
        catch (Exception e)
        {
            Log.Warn($"Caret context read failed: {e.Message}");
            return (text, string.Empty);
        }
    }

    /// <summary>
    /// Types the most recent transcript again into whatever has focus now.
    /// </summary>
    /// <remarks>
    /// The recovery for text that landed in the wrong window, which otherwise means opening
    /// the history, copying and pasting by hand.
    /// </remarks>
    /// <returns>False if there is nothing to retype or it could not be delivered.</returns>
    public async Task<bool> RetypeAsync(string text)
    {
        if (string.IsNullOrEmpty(text) || State != DictationState.Idle) return false;
        var delivered = await _injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(false);
        Log.Info($"retyped last transcript; accepted={delivered}");
        if (!delivered) Fault("The text could not be typed into the focused app.");
        return delivered;
    }

    private async Task SendToFocusedAppAsync(string reason)
    {
        if (await _injector.SendAsync(CancellationToken.None).ConfigureAwait(false))
        {
            Log.Info($"Enter sent ({reason})");
            // Whatever was typed has gone; the next dictation starts a new message.
            _lastDelivery = null;
            Sent?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Fault("Enter could not be pressed. Send the text manually.");
        }
    }

    private DateTimeOffset _lastFaultAt = DateTimeOffset.MinValue;

    /// <summary>Whether <see cref="Faulted"/> fired within the last second, so a follow-up notice can defer to it.</summary>
    public bool IsFaultedRecently => _clock.Now - _lastFaultAt < TimeSpan.FromSeconds(1);

    private (string Text, bool Send) ExtractSendCommand(string text)
    {
        var phrase = SpokenSendCommand.Extract(text, SendOnlyPhrase);
        return phrase.Send ? phrase : SpokenSendCommand.Extract(text, SendWord, SendWordAliases);
    }

    private void Fault(string message)
    {
        LastFault = message;
        _lastFaultAt = _clock.Now;
        Faulted?.Invoke(this, message);
    }

    private void RestoreAudio()
    {
        lock (_audioLock)
        {
            if (!_ducked) return;
            _ducked = false;
            Ducker?.Restore();
            Log.Info("other audio restored");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;
        _hotkey.CancelPressed -= OnCancelPressed;
        _hotkey.Dispose();

        // Read under the gate: EndAsync flips the state to Transcribing and only then, still
        // holding the gate, queues the transcription on _finishChain. Read outside it, a
        // dispose that lands in that gap sees the previous, completed chain, waits for
        // nothing, and pulls the model out from under the decode that is about to start.
        Session? current;
        Task finishChain;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            current = _current;
            if (current is not null) lock (_bufferLock) _current = null;
            finishChain = _finishChain;
        }
        finally
        {
            _gate.Release();
        }
        if (current is not null) await current.Stop.CancelAsync().ConfigureAwait(false);

        // A quit during a dictation must not pull the model out from under a decode that
        // is still running on a pool thread — that is a native use-after-free, not an
        // exception. Wait for the preview and the transcription queue, briefly.
        var inFlight = new[] { current?.Preview, current?.CaptureLoop, finishChain }.Where(t => t is not null).Select(t => t!).ToArray();
        try { await Task.WhenAll(inFlight).WaitAsync(DisposeGrace).ConfigureAwait(false); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException) { Log.Warn("shutdown did not wait for the dictation in flight"); }

        current?.Dispose();

        // Belt and braces: a recording cut short by shutdown must not leave the user's
        // music at a whisper.
        RestoreAudio();

        await _capture.DisposeAsync().ConfigureAwait(false);
        await _transcriber.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
