using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Speech;

namespace Murmur.App;

/// <summary>
/// Wires the app together: settings, storage, the platform layer, and the engine.
/// </summary>
/// <remarks>
/// <para>
/// The only place that knows about every layer at once. Views take what they need; nothing
/// else reaches across.
/// </para>
/// <para>
/// The platform implementations are resolved by reflection rather than referenced directly,
/// so <c>Murmur.App</c> can target plain <c>net10.0</c> and therefore be built, run and
/// headless-tested on macOS — which is the entire reason Avalonia was chosen over WPF. On any
/// non-Windows machine the lookup simply finds nothing and the app runs with inert stand-ins,
/// which is exactly what a UI test wants.
/// </para>
/// </remarks>
public sealed class Composition : IAsyncDisposable
{
    private Composition(
        AppSettings settings,
        DictionaryFile dictionary,
        TranscriptStore transcripts,
        DictationEngine? engine,
        ReloadableTranscriber? transcriber,
        IStartupRegistration? startup,
        IAudioDeviceCatalog? devices,
        bool platformAvailable)
    {
        Settings = settings;
        Dictionary = dictionary;
        Transcripts = transcripts;
        Engine = engine;
        Transcriber = transcriber;
        Startup = startup;
        Devices = devices;
        IsPlatformAvailable = platformAvailable;
    }

    /// <summary>Microphone enumeration, or null where the platform offers none.</summary>
    public IAudioDeviceCatalog? Devices { get; }

    /// <summary>User preferences.</summary>
    public AppSettings Settings { get; }

    /// <summary>The correction dictionary.</summary>
    public DictionaryFile Dictionary { get; }

    /// <summary>Transcript history.</summary>
    public TranscriptStore Transcripts { get; }

    /// <summary>The dictation engine, or null when no platform layer is available.</summary>
    public DictationEngine? Engine { get; }

    /// <summary>The speech engine wrapper, so Settings can ask whether a model is loaded.</summary>
    public ReloadableTranscriber? Transcriber { get; }

    /// <summary>Start-at-sign-in, or null where the platform offers none.</summary>
    public IStartupRegistration? Startup { get; }

    /// <summary>Whether real audio and hotkey support were found.</summary>
    public bool IsPlatformAvailable { get; }

    /// <summary>Whether the speech model is present on disk.</summary>
    public static bool IsModelInstalled => ParakeetTranscriber.Locate() is not null;

    /// <summary>Builds the object graph.</summary>
    public static Composition Create()
    {
        AppPaths.MigrateLegacyFolder();
        Log.Info($"{AppPaths.ProductName} starting: {Environment.ProcessPath} on {Environment.OSVersion}");

        // The platform layer cannot reference the log; give it a way in. Before this, a
        // ducker that had given up for good said so only in a Debug build.
        PlatformDiagnostics.Sink = message => Log.Warn($"platform: {message}");

        var settings = new AppSettings(AppSettings.DefaultPath);
        var dictionary = new DictionaryFile(DictionaryFile.DefaultPath);
        var transcripts = new TranscriptStore(TranscriptStore.DefaultPath);

        // Warm: the microphone stays open between dictations so the first word is never lost
        // to device start-up, and the moments before the key press are included.
        WarmAudioCapture? capture = PlatformFactory.CreateAudioCapture(() => settings.Data.MicrophoneDeviceId) is { } device
            ? new WarmAudioCapture(device)
            : null;

        // A different microphone chosen in Settings must be the one the next recording
        // uses; a warm stream would otherwise keep the old device open for minutes.
        var lastMicrophone = settings.Data.MicrophoneDeviceId;
        settings.Changed += (_, _) =>
        {
            if (settings.Data.MicrophoneDeviceId == lastMicrophone) return;
            lastMicrophone = settings.Data.MicrophoneDeviceId;
            capture?.ReopenDevice();
        };
        var hotkey = PlatformFactory.CreateHotkeySource(settings.Data.PushToTalkKey);
        var injector = PlatformFactory.CreateTextInjector();
        var startup = PlatformFactory.CreateStartupRegistration();
        var devices = PlatformFactory.CreateAudioDeviceCatalog();

        DictationEngine? engine = null;
        ReloadableTranscriber? transcriber = null;
        var available = capture is not null && hotkey is not null && injector is not null;

        Log.Info(available ? "platform layer loaded" : "platform layer NOT available — running inert");

        if (available)
        {
            // Resolved lazily so a model downloaded from Settings is picked up without a
            // restart. An explicit ModelDirectory in settings wins over the search paths.
            transcriber = new ReloadableTranscriber(
                () => settings.Data.ModelDirectory is { } chosen && ParakeetTranscriber.IsComplete(chosen)
                    ? chosen
                    : ParakeetTranscriber.Locate(),
                directory => new ParakeetTranscriber(directory));

            engine = new DictationEngine(
                capture!, hotkey!, transcriber, injector!,
                // The engine reads this on a pool thread mid-transcription; DictionaryFile
                // hands back an immutable snapshot, so a click in the editor cannot race it.
                () => dictionary.Entries)
            {
                InjectText = settings.Data.InjectText,
                SendWord = settings.Data.SendWord,
                SendWordAliases = settings.Data.SendWordAliases,
                SendOnlyPhrase = settings.Data.SendOnlyPhrase,
                SendOnlyAliases = settings.Data.SendOnlyAliases,
                FullStops = settings.Data.FullStops,
                Mode = settings.Data.Mode,
                SpokenCommands = settings.Data.SpokenCommands,
                RemoveFillers = settings.Data.RemoveFillers,
                NoCommaBeforeAnd = settings.Data.NoCommaBeforeAnd,
                BritishSpelling = settings.Data.BritishSpelling,
                ReviewWholeDictation = settings.Data.ReviewWholeDictation,
                JoinDictations = settings.Data.JoinDictations,
                Ducker = PlatformFactory.CreateAudioDucker(),
                DuckAudio = settings.Data.DuckOtherAudio,
                AiCleanup = settings.Data.AiCleanup,
                IsEnabled = settings.Data.IsEnabled,
                HotkeyModifiers = settings.Data.PushToTalkModifiers,
                // Key and model are read per call, so pasting a key into Settings works at once.
                Cleaner = NewCleaner(settings, dictionary),
                CloudTranscriber = NewCloudTranscriber(settings),
                Decisions = new JevClient(() => settings.Data.JevApiKey, settings.Data.JevBaseUrl, settings.Data.JevModel),
            };

            var jevBase = settings.Data.JevBaseUrl;
            settings.Changed += (_, _) =>
            {
                engine.InjectText = settings.Data.InjectText;
                engine.SendWord = settings.Data.SendWord;
                engine.SendWordAliases = settings.Data.SendWordAliases;
                engine.SendOnlyPhrase = settings.Data.SendOnlyPhrase;
                engine.SendOnlyAliases = settings.Data.SendOnlyAliases;
                engine.FullStops = settings.Data.FullStops;
                engine.Mode = settings.Data.Mode;
                engine.SpokenCommands = settings.Data.SpokenCommands;
                engine.RemoveFillers = settings.Data.RemoveFillers;
                engine.NoCommaBeforeAnd = settings.Data.NoCommaBeforeAnd;
                engine.BritishSpelling = settings.Data.BritishSpelling;
                engine.ReviewWholeDictation = settings.Data.ReviewWholeDictation;
                engine.JoinDictations = settings.Data.JoinDictations;
                engine.DuckAudio = settings.Data.DuckOtherAudio;
                engine.AiCleanup = settings.Data.AiCleanup;
                engine.IsEnabled = settings.Data.IsEnabled;
                // Idempotent both ways: warm stays warm, released stays released.
                if (settings.Data.IsEnabled) capture!.WarmUp(); else capture!.Release();
                if (engine.HotkeyVirtualKey != settings.Data.PushToTalkKey) engine.HotkeyVirtualKey = settings.Data.PushToTalkKey;
                if (engine.HotkeyModifiers != settings.Data.PushToTalkModifiers) engine.HotkeyModifiers = settings.Data.PushToTalkModifiers;
                if (engine.CloudTranscriber?.Name != NewCloudTranscriber(settings)?.Name) engine.CloudTranscriber = NewCloudTranscriber(settings);
                if (engine.Cleaner?.Name != (string.IsNullOrWhiteSpace(settings.Data.GeminiModel) ? GeminiCleaner.DefaultModel : settings.Data.GeminiModel.Trim()))
                {
                    (engine.Cleaner as IDisposable)?.Dispose();
                    engine.Cleaner = NewCleaner(settings, dictionary);
                }
                // The key is read per call; only a new base or model needs a new client.
                if (engine.Decisions is JevClient jev && (jev.Name != (string.IsNullOrWhiteSpace(settings.Data.JevModel) ? JevClient.DefaultModel : settings.Data.JevModel.Trim()) || jevBase != settings.Data.JevBaseUrl))
                {
                    jevBase = settings.Data.JevBaseUrl;
                    jev.Dispose();
                    engine.Decisions = new JevClient(() => settings.Data.JevApiKey, settings.Data.JevBaseUrl, settings.Data.JevModel);
                }
            };

            // Open the microphone now rather than on the first key press: a cold stream
            // spent up to 900 ms delivering silence, which is where first words went.
            if (settings.Data.IsEnabled) capture!.WarmUp();

            var archive = new RecordingArchive(RecordingArchive.DefaultFolder);
            engine.Completed += (_, result) =>
            {
                if (!settings.Data.KeepHistory) return;

                // Off the typing path: the text is already in the field by now.
                if (settings.Data.KeepRecordings && !result.Audio.IsEmpty) _ = Task.Run(() => archive.Save(result.At, result.Audio));

                transcripts.Add(new TranscriptRecord
                {
                    At = result.At,
                    AudioSeconds = result.AudioDuration.TotalSeconds,
                    ProcessingSeconds = result.ProcessingTime.TotalSeconds,
                    Text = result.Text,
                    Corrections = result.Corrections.Count > 0 ? result.Corrections : null,
                    CleanedBy = result.CleanedBy,
                    RawText = result.RawText,
                    CleanupFailed = result.CleanupFailed,
                    TranscribedBy = result.TranscribedBy,
                    LocalRawText = result.LocalRawText,
                });
            };
        }

        return new Composition(settings, dictionary, transcripts, engine, transcriber, startup, devices, available);
    }

    /// <summary>
    /// The AI tier over the current settings and dictionary. The dictionary's correct
    /// spellings go to the model as vocabulary: the speech model on Windows cannot be
    /// biased, so this is the one tier that can hear "get pool" as "git pull".
    /// </summary>
    /// <summary>The cloud speech-to-text over the current settings, or null when it is off.</summary>
    private static IStreamingTranscriber? NewCloudTranscriber(AppSettings settings) =>
        !settings.Data.CloudTranscription ? null
        : string.Equals(settings.Data.CloudTranscriptionProvider, "gemini", StringComparison.OrdinalIgnoreCase)
            ? new GeminiLiveTranscriber(() => settings.Data.GeminiApiKey, settings.Data.CloudTranscriptionModel, settings.Data.BritishSpelling ? "en-GB" : "en-US")
            : new ElevenLabsTranscriber(() => settings.Data.ElevenLabsApiKey, settings.Data.CloudTranscriptionModel);

    private static GeminiCleaner NewCleaner(AppSettings settings, DictionaryFile dictionary) =>
        new(() => settings.Data.GeminiApiKey, settings.Data.GeminiModel,
            customInstructions: () => settings.Data.CustomInstructions,
            vocabulary: () => DictionaryCorrector.BiasPhrases(dictionary.Entries));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Engine is not null) await Engine.DisposeAsync().ConfigureAwait(false);
        Log.Info($"{AppPaths.ProductName} stopped");
    }
}
