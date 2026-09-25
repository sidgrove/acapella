using Murmur.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Core;

/// <summary>User preferences.</summary>
/// <remarks>
/// Plain setters, not <c>init</c>: with source-generated JSON an <c>init</c> property that is
/// absent from an older settings file came back as <c>false</c>, not its declared default —
/// which silently switched the app off for anyone upgrading. <c>with</c> expressions work
/// either way.
/// </remarks>
public sealed record SettingsData
{
    /// <summary>
    /// Virtual-key code of the push-to-talk key. Defaults to Right Ctrl (0xA3).
    /// </summary>
    /// <remarks>
    /// <b>Not Right Alt.</b> On German, Polish, UK, Nordic and most Latin-American layouts
    /// Right Alt is AltGr — it is how those users type <c>@</c>, <c>€</c>, <c>\</c> and
    /// <c>|</c>. Right Ctrl produces no character on any layout.
    /// </remarks>
    public int PushToTalkKey { get; set; } = 0xA3;

    /// <summary>
    /// Modifiers that must be held with <see cref="PushToTalkKey"/>, as
    /// <see cref="Murmur.Abstractions.HotkeyModifiers"/> flags. Zero for a bare key.
    /// </summary>
    public int PushToTalkModifiers { get; set; }

    /// <summary>Where the speech model lives, or null to search the default locations.</summary>
    public string? ModelDirectory { get; set; }

    /// <summary>
    /// The microphone to record from, as an OS device id, or null for the system's default
    /// communications device.
    /// </summary>
    /// <remarks>
    /// Read on every recording rather than once at startup, so picking a different
    /// microphone in Settings takes effect on the next key press.
    /// </remarks>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>Whether to type the transcript into the focused app.</summary>
    public bool InjectText { get; set; } = true;

    /// <summary>Play a short cue when recording starts or stops.</summary>
    public bool RecordingSounds { get; set; } = true;

    /// <summary>Play a confirmation after Enter is sent.</summary>
    public bool SendSound { get; set; } = true;

    /// <summary>Terminal spoken command that presses Enter; blank disables it.</summary>
    public string SendWord { get; set; } = "blob";

    /// <summary>Comma-separated alternative words recognised only at the end of dictation.</summary>
    public string SendWordAliases { get; set; } = string.Empty;

    /// <summary>Terminal send phrase; spoken alone it sends existing text. Blank disables it.</summary>
    public string SendOnlyPhrase { get; set; } = "send it";

    /// <summary>
    /// Comma-separated mishearings of <see cref="SendOnlyPhrase"/> that count as it when they
    /// are the whole utterance. Add whatever the history shows the model hearing instead.
    /// </summary>
    public string SendOnlyAliases { get; set; } = SpokenSendCommand.DefaultSendOnlyAliases;

    /// <summary>Whether to keep a transcript history.</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>
    /// Keep the audio of the last month's dictations on this PC, so a change of model or
    /// prompt can be replayed over real speech and scored. Nothing is uploaded.
    /// </summary>
    public bool KeepRecordings { get; set; }

    /// <summary>
    /// Legacy. Superseded by <see cref="FullStops"/>; read once at load to migrate an older
    /// file, then always written back as true. Nothing else reads it.
    /// </summary>
    public bool DropSingleSentenceFullStop { get; set; } = true;

    /// <summary>
    /// Legacy. Superseded by <see cref="Mode"/>; read once at load to migrate an older
    /// file, then always written back as false. Nothing else reads it.
    /// </summary>
    public bool TapToToggle { get; set; }

    /// <summary>Whether transcripts go through the generative clean-up before typing.</summary>
    public bool AiCleanup { get; set; }

    /// <summary>Gemini API key, or null to use the <c>GEMINI_API_KEY</c> environment variable.</summary>
    public string? GeminiApiKey { get; set; }

    /// <summary>Gemini model id, or null for the default.</summary>
    public string? GeminiModel { get; set; }

    /// <summary>
    /// Stream the audio to a cloud speech-to-text model while the key is held. Its reading
    /// and the local model's both go to the clean-up, which takes each word from whichever
    /// makes more sense; the local model alone stands in whenever the cloud fails.
    /// </summary>
    public bool CloudTranscription { get; set; }

    /// <summary>
    /// "elevenlabs" (Scribe v2 Realtime, the default) or "gemini" (3.5 Transcribe Live).
    /// On 41 of Dave's dictations on 2026-09-24, Scribe paired with Parakeet left 3.5% of
    /// words wrong after clean-up, Gemini with Parakeet 3.8% and Parakeet alone 4.9%.
    /// </summary>
    public string? CloudTranscriptionProvider { get; set; }

    /// <summary>Cloud speech-to-text model id, or null for the provider's default.</summary>
    public string? CloudTranscriptionModel { get; set; }

    /// <summary>ElevenLabs key, or null to use the <c>ELEVENLABS_API_KEY</c> environment variable.</summary>
    public string? ElevenLabsApiKey { get; set; }

    /// <summary>
    /// Key for Jev, the decision model, or null to use the <c>AI_GATEWAY_API_KEY</c>
    /// environment variable. With no key the app's own rules decide everything, as before.
    /// </summary>
    public string? JevApiKey { get; set; }

    /// <summary>Where Jev is reached. Null for Vercel's AI Gateway; <c>https://api.typesafe.ai</c> for TypeSafe directly.</summary>
    public string? JevBaseUrl { get; set; }

    /// <summary>Jev model id, or null for the default at the chosen base.</summary>
    public string? JevModel { get; set; }

    /// <summary>Whether the push-to-talk key does anything. Off pauses the app without quitting.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>How the key works. <see cref="TapToToggle"/> from older files maps onto <see cref="ActivationMode.Tap"/>.</summary>
    public ActivationMode Mode { get; set; } = ActivationMode.Automatic;

    /// <summary>What happens to a full stop at the very end of a dictation.</summary>
    public TrailingFullStop FullStops { get; set; } = TrailingFullStop.DropAfterSingleSentence;

    /// <summary>Whether "new line", "full stop", "scratch that" and so on are applied locally.</summary>
    public bool SpokenCommands { get; set; } = true;

    /// <summary>Whether "um", "er" and friends are removed locally.</summary>
    public bool RemoveFillers { get; set; } = true;

    /// <summary>Whether other applications' playback is turned down while recording.</summary>
    public bool DuckOtherAudio { get; set; } = true;

    /// <summary>House style: no comma before "and", enforced after the AI tier as well.</summary>
    public bool NoCommaBeforeAnd { get; set; }

    /// <summary>Write the speech model's American spellings the British way.</summary>
    public bool BritishSpelling { get; set; } = true;

    /// <summary>
    /// Clean a long dictation in one pass once the key is up, so the model sees all of it,
    /// rather than piece by piece while it is still being spoken. Slower on long dictations
    /// (roughly a second per hundred words) and the best result.
    /// </summary>
    public bool ReviewWholeDictation { get; set; }

    /// <summary>
    /// When the next dictation goes into the same field straight after the last, type the
    /// space, and the full stop the trailing rule removed, that joins them.
    /// </summary>
    public bool JoinDictations { get; set; } = true;

    /// <summary>
    /// Show the AI clean-up the app, window title and text before the caret, so names on
    /// screen are spelt the same way.
    /// </summary>
    public bool CleanupSeesScreen { get; set; } = true;

    /// <summary>
    /// After typing, watch the field for a couple of minutes to see what the user changed:
    /// the result is kept in the history and sound-alike fixes become dictionary suggestions.
    /// </summary>
    public bool LearnFromEdits { get; set; } = true;

    /// <summary>The user's own rules for the AI clean-up, appended to the prompt. Null for none.</summary>
    public string? CustomInstructions { get; set; }

    /// <summary>Whether the first-run walkthrough has been completed or dismissed.</summary>
    public bool HasOnboarded { get; set; }
}

/// <summary>Settings, persisted as JSON.</summary>
public sealed class AppSettings
{
    private readonly string _path;

    /// <summary>Loads settings from <paramref name="path"/>, or defaults if absent.</summary>
    public AppSettings(string path)
    {
        _path = path;
        Data = Load(path);
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "settings.json");

    /// <summary>Current values.</summary>
    public SettingsData Data { get; private set; }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Replaces and persists the settings.
    /// </summary>
    /// <remarks>
    /// The in-memory value is updated even when the disk write fails — an antivirus scan
    /// or a sync client holding the file for a moment must not undo a change the user just
    /// made, and must not crash the text box they made it in. The failure goes to the log
    /// and the next save tries again.
    /// </remarks>
    public void Update(SettingsData data)
    {
        Data = data;

        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(data, SettingsJsonContext.Default.SettingsData));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"settings could not be saved: {e.Message}");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static SettingsData Load(string path)
    {
        // Corrupt or unreadable settings must never stop the app launching — defaults are
        // always a working configuration.
        try
        {
            if (!File.Exists(path)) return new SettingsData();

            var data = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.SettingsData)
                       ?? new SettingsData();

            // Files written before activation modes and the full-stop rule existed carry
            // only the two old switches. Map each once, then neutralise it, so the mapping
            // cannot re-apply on a later launch and silently undo a choice made since.
            if (data.TapToToggle)
            {
                if (data.Mode == ActivationMode.Automatic) data.Mode = ActivationMode.Tap;
                data.TapToToggle = false;
            }
            if (!data.DropSingleSentenceFullStop)
            {
                if (data.FullStops == TrailingFullStop.DropAfterSingleSentence) data.FullStops = TrailingFullStop.Keep;
                data.DropSingleSentenceFullStop = true;
            }
            return data;
        }
        catch (JsonException e)
        {
            Log.Warn($"settings file was unreadable and has been set aside: {e.Message}");
            AtomicFile.SetAside(path);
            return new SettingsData();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"settings could not be read: {e.Message}");
            return new SettingsData();
        }
    }
}

/// <summary>Source-generated JSON for settings.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsData))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
