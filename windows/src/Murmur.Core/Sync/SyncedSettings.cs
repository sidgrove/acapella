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
    /// The settings another PC sent, read leniently: a field it did not send keeps its
    /// default. Null if <paramref name="data"/> is not a settings object.
    /// </summary>
    public static SettingsData? Read(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        try
        {
            return data.Deserialize(SyncJsonContext.Default.SettingsData);
        }
        catch (JsonException)
        {
            return null;
        }
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
