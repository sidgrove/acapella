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
