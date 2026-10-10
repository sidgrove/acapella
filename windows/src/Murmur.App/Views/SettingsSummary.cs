using System.Globalization;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Core.Sync;
using Murmur.Speech;

namespace Murmur.App.Views;

/// <summary>
/// What each section of Settings is set to, in a few plain words, for its folded row. Every line
/// is worked out from the stored settings and the state of the machine, never typed in; a section
/// that is missing something it needs says so instead of describing itself.
/// </summary>
/// <remarks>
/// Pure functions of what they are given, so each has a test that changes a setting and reads the
/// line change with it. A summary that is wrong once is worse than none.
/// </remarks>
public static class SettingsSummary
{
    /// <summary>A row's summary, and whether it is reporting a problem.</summary>
    public sealed record Line(string Text, bool Problem = false);

    /// <summary>The key and how it is used: "Right Ctrl, hold or tap".</summary>
    public static Line PushToTalk(SettingsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var how = data.Mode switch
        {
            ActivationMode.Tap => "tap to start and stop",
            ActivationMode.Hold => "hold to talk",
            _ => "hold or tap",
        };
        return new($"{KeyNames.Describe(data.PushToTalkKey, data.PushToTalkModifiers)}, {how}");
    }

    /// <summary>Which microphone is used, or why there is none to use.</summary>
    /// <param name="data">The settings.</param>
    /// <param name="devices">The microphones Windows lists, or null where they cannot be listed.</param>
    public static Line Microphone(SettingsData data, IReadOnlyList<AudioDevice>? devices)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (devices is null) return new("Windows default");
        if (devices.Count == 0) return new("No microphone found", Problem: true);
        if (data.MicrophoneDeviceId is not { } chosen) return new("Windows default");
        return devices.FirstOrDefault(d => d.Id == chosen) is { } device
            ? new(device.Name)
            : new("Yours is not connected, so the Windows default is used", Problem: true);
    }

    /// <summary>Which of the two cues are on.</summary>
    public static Line Sounds(SettingsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new((data.RecordingSounds, data.SendSound) switch
        {
            (true, true) => "Recording and send sounds on",
            (true, false) => "Recording sounds only",
            (false, true) => "Send sound only",
            _ => "Off",
        });
    }

    /// <summary>Where the words are heard: on this PC, and in the cloud as well when that is on.</summary>
    /// <param name="data">The settings.</param>
    /// <param name="modelInstalled">Whether the speech model is on disk.</param>
    /// <param name="modelReady">Whether it has loaded.</param>
    /// <param name="downloading">Whether it is being downloaded now.</param>
    /// <param name="cloudKey">Whether there is an ElevenLabs key from anywhere.</param>
    public static Line Hearing(SettingsData data, bool modelInstalled, bool modelReady, bool downloading, bool cloudKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (downloading) return new("Downloading the speech model");
        if (!modelInstalled) return new("The speech model is not downloaded yet", Problem: true);
        if (data.CloudTranscription && !cloudKey) return new("On this PC. The cloud is on but has no key", Problem: true);
        var where = data.CloudTranscription ? "On this PC and in the cloud" : "On this PC";
        return new(modelReady ? where : $"{where}, loading");
    }

    /// <summary>The full stop rule, and how many of the five writing rules are on.</summary>
    public static Line Writing(SettingsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var stop = data.FullStops switch
        {
            TrailingFullStop.Never => "Never ends with a full stop",
            TrailingFullStop.Keep => "Full stops kept",
            _ => "Full stop dropped after one sentence",
        };
        bool[] rules = [data.SpokenCommands, data.RemoveFillers, data.NoCommaBeforeAnd, data.BritishSpelling, data.JoinDictations];
        var on = rules.Count(r => r);
        return new(string.Create(CultureInfo.InvariantCulture, $"{stop}, {on} of {rules.Length} rules on"));
    }

    /// <summary>What to say to send: the word, the phrase, both or nothing.</summary>
    public static Line Sending(SettingsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var word = data.SendWord.Trim();
        var phrase = data.SendOnlyPhrase.Trim();
        return new((word.Length > 0, phrase.Length > 0) switch
        {
            (true, true) => $"Say “{word}” or “{phrase}”",
            (true, false) => $"Say “{word}”",
            (false, true) => $"Say “{phrase}”",
            _ => "Off",
        });
    }

    /// <summary>Instant or Polished, with which model, or that the model it needs has no key.</summary>
    /// <param name="data">The settings.</param>
    /// <param name="geminiKey">Whether there is a Gemini key from anywhere: typed, from Sidgrove Intelligence or on this PC.</param>
    /// <param name="anthropicKey">The same for Anthropic.</param>
    public static Line CleanUp(SettingsData data, bool geminiKey, bool anthropicKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.AiCleanup) return new("Instant, typed as heard");
        var model = string.IsNullOrWhiteSpace(data.GeminiModel) ? GeminiCleaner.DefaultModel : data.GeminiModel.Trim();
        var claude = ClaudeCleaner.Serves(model);
        if (claude ? !anthropicKey : !geminiKey)
            return new($"Polished, but there is no {(claude ? "Anthropic" : "Gemini")} key, so words are typed as heard", Problem: true);
        var own = string.IsNullOrWhiteSpace(data.CustomInstructions) ? string.Empty : ", with your own instructions";
        return new($"Polished by {model}{own}");
    }

    /// <summary>Whether it learns from edits, and what it does with a fix.</summary>
    public static Line Learning(SettingsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.LearnFromEdits) return new(data.KeepRecordings ? "Off, but corrected recordings are kept" : "Off");
        var fixes = data.AddLearntFixes ? "On, adds sure fixes by itself" : "On, asks before adding a word";
        return new(data.KeepRecordings ? $"{fixes}, keeps corrected recordings" : fixes);
    }

    /// <summary>On, or off because there is no key from anywhere.</summary>
    /// <param name="key">Whether there is a gateway key: typed, from Sidgrove Intelligence or on this PC.</param>
    public static Line Jev(bool key) => new(key ? "On" : "Off, with no key from anywhere");

    /// <summary>Who is signed in and when it last synced, or what is wrong.</summary>
    public static Line Sync(SyncStatus? status, DateTimeOffset now)
    {
        if (status is null || !status.IsSignedIn) return new("Not signed in");
        if (!string.IsNullOrEmpty(status.Problem)) return new(status.Problem, Problem: true);
        var when = status.IsRunning ? "syncing now"
            : status.LastSyncedAt is null ? "not synced yet"
            : $"synced {SyncPart.Relative(status.LastSyncedAt, now)}";
        return new($"{status.Email}, {when}");
    }

    /// <summary>Where the words go and what is kept.</summary>
    /// <param name="data">The settings.</param>
    /// <param name="startsWithWindows">Whether it starts at sign-in, or null where that cannot be set.</param>
    public static Line Behaviour(SettingsData data, bool? startsWithWindows)
    {
        ArgumentNullException.ThrowIfNull(data);
        var parts = new List<string>
        {
            data.InjectText ? "Types into your app" : "Copies to the clipboard only",
            data.KeepHistory ? "keeps a history" : "no history",
        };
        if (data.DuckOtherAudio) parts.Add("mutes other audio");
        if (startsWithWindows == true) parts.Add("starts with Windows");
        return new(string.Join(", ", parts));
    }
}
