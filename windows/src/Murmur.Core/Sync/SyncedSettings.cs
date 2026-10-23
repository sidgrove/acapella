using System.Text.Json;
using System.Text.Json.Nodes;

namespace Murmur.Core.Sync;

/// <summary>
/// Which settings travel between PCs and which belong to this one.
/// </summary>
/// <remarks>
/// Everything syncs, API keys included (Dave, 02/10/2026), except what only makes sense on
/// the PC it was set on: the microphone, the model folder, the hotkey, the welcome sheet,
/// the on/off switch, and sync's own switch and server. The two legacy switches stay home
/// too; each is rewritten to a fixed value at load and means nothing anywhere else.
/// </remarks>
public static class SyncedSettings
{
    /// <summary>The fields that never leave this PC, by their JSON names.</summary>
    public static IReadOnlyList<string> MachineFields { get; } =
    [
        nameof(SettingsData.MicrophoneDeviceId),
        nameof(SettingsData.ModelDirectory),
        nameof(SettingsData.PushToTalkKey),
        nameof(SettingsData.PushToTalkModifiers),
        nameof(SettingsData.HasOnboarded),
        nameof(SettingsData.IsEnabled),
        nameof(SettingsData.SyncEnabled),
        nameof(SettingsData.SyncServer),
        nameof(SettingsData.DropSingleSentenceFullStop),
        nameof(SettingsData.TapToToggle),
    ];

    /// <summary>The synced fields of a fresh install, which a PC never pushes until it has synced once.</summary>
    public static string Defaults { get; } = Canonical(new SettingsData());

    /// <summary>The synced fields of <paramref name="data"/> as compact JSON, in a fixed order.</summary>
    public static string Canonical(SettingsData data)
    {
        var node = JsonSerializer.SerializeToNode(data, SyncJsonContext.Default.SettingsData)!.AsObject();
        foreach (var field in MachineFields) node.Remove(field);
        return node.ToJsonString();
    }

    /// <summary>
    /// The settings another PC sent, read leniently: a field it did not send, because it runs
    /// a build older than that setting, keeps <paramref name="here"/>'s value rather than
    /// falling back to the default. Null if <paramref name="data"/> is not a settings object.
    /// </summary>
    public static SettingsData? Read(JsonElement data, SettingsData here)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var node = JsonSerializer.SerializeToNode(here, SyncJsonContext.Default.SettingsData)!.AsObject();
            foreach (var property in data.EnumerateObject()) node[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            return node.Deserialize(SyncJsonContext.Default.SettingsData);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// For a PC's first sync, when nothing has been agreed with the server yet: the server's
    /// canonical settings, except that a field this PC changed from the default where the
    /// server still holds the default keeps this PC's value. A fresh install takes everything;
    /// a PC that has been in use keeps what was chosen on it. Where both chose, the server's
    /// value stands. <paramref name="kept"/> names the fields kept.
    /// </summary>
    public static string KeepChoicesMadeHere(string server, string here, out IReadOnlyList<string> kept)
    {
        var result = JsonNode.Parse(server)!.AsObject();
        var mine = JsonNode.Parse(here)!.AsObject();
        var defaults = JsonNode.Parse(Defaults)!.AsObject();
        var names = new List<string>();
        foreach (var (name, value) in mine)
        {
            if (JsonNode.DeepEquals(value, defaults[name]) || !JsonNode.DeepEquals(result[name], defaults[name])) continue;
            result[name] = value?.DeepClone();
            names.Add(name);
        }
        kept = names;
        return names.Count == 0 ? server : Canonical(result.Deserialize(SyncJsonContext.Default.SettingsData)!);
    }

    /// <summary>
    /// The synced fields that differ between two canonical forms, for the log: on/off and
    /// numbers with their values, everything else (API keys, free text) by name only.
    /// </summary>
    public static IReadOnlyList<string> Describe(string before, string after)
    {
        var old = JsonNode.Parse(before)!.AsObject();
        var changes = new List<string>();
        foreach (var (name, value) in JsonNode.Parse(after)!.AsObject())
        {
            var was = old[name];
            if (JsonNode.DeepEquals(was, value)) continue;
            changes.Add(value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number
                ? $"{name} from {Show(was)} to {Show(value)}"
                : name);
        }
        return changes;

        static string Show(JsonNode? node) => node?.GetValueKind() switch
        {
            JsonValueKind.True => "on",
            JsonValueKind.False => "off",
            null or JsonValueKind.Null => "nothing",
            _ => node.ToJsonString(),
        };
    }

    /// <summary><paramref name="incoming"/>'s synced fields with this PC's own machine fields.</summary>
    public static SettingsData Merge(SettingsData incoming, SettingsData local) => incoming with
    {
        MicrophoneDeviceId = local.MicrophoneDeviceId,
        ModelDirectory = local.ModelDirectory,
        PushToTalkKey = local.PushToTalkKey,
        PushToTalkModifiers = local.PushToTalkModifiers,
        HasOnboarded = local.HasOnboarded,
        IsEnabled = local.IsEnabled,
        SyncEnabled = local.SyncEnabled,
        SyncServer = local.SyncServer,
        DropSingleSentenceFullStop = local.DropSingleSentenceFullStop,
        TapToToggle = local.TapToToggle,
    };
}
