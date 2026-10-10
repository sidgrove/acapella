using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Dictionary;

namespace Murmur.Core.Sync;

/// <summary>
/// What this PC last agreed with the server: the cursor, a hash per (kind, key) as last
/// synced, and the recordings still waiting to be downloaded. Kept in <c>sync-state.json</c>.
/// </summary>
/// <remarks>
/// Change detection is by snapshot (docs/sync.md): an item whose hash differs from the one
/// here changed on this PC since the last sync, and a hash here with no item behind it is
/// a delete. Nothing in the stores has to know about sync for that to work.
/// </remarks>
public sealed class SyncState
{
    /// <summary>The cursor to send next.</summary>
    public long Cursor { get; set; }

    /// <summary>
    /// Whether the first full pull has been done. Until then a pulled row beats the local
    /// copy, so a new PC's default settings cannot overwrite the ones every other PC has.
    /// </summary>
    public bool Primed { get; set; }

    /// <summary>When a sync last finished, for the Settings card.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>The hash of each item as last synced, by <c>kind|key</c>.</summary>
    public Dictionary<string, string> Items { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Recordings another PC has that this one has not fetched yet, by history record key, with the record's time.</summary>
    public Dictionary<string, DateTimeOffset> PendingDownloads { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Reads the state at <paramref name="path"/>, or a fresh one if it is missing or unreadable.</summary>
    public static SyncState Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new SyncState();
            var state = JsonSerializer.Deserialize(File.ReadAllText(path), SyncJsonContext.Default.SyncState) ?? new SyncState();
            // Deserialised dictionaries lose the comparer; ordinal is what every lookup assumes.
            state.Items = new Dictionary<string, string>(state.Items, StringComparer.Ordinal);
            state.PendingDownloads = new Dictionary<string, DateTimeOffset>(state.PendingDownloads, StringComparer.Ordinal);
            return state;
        }
        catch (JsonException e)
        {
            Log.Warn($"sync: state was unreadable and has been set aside, starting afresh: {e.Message}");
            AtomicFile.SetAside(path);
            return new SyncState();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"sync: state could not be read: {e.Message}");
            return new SyncState();
        }
    }

    /// <summary>Writes the state atomically. A failure goes to the log; the next run tries again.</summary>
    public void Save(string path)
    {
        try
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, SyncJsonContext.Default.SyncState));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"sync: state could not be saved: {e.Message}");
        }
    }

    /// <summary>The state key for an item.</summary>
    public static string Id(string kind, string key) => kind + "|" + key;

    /// <summary>A short, stable hash of an item's canonical JSON.</summary>
    public static string Hash(string json) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(json)).AsSpan(0, 16));
}

/// <summary>The <c>data</c> of a <c>dictionary</c> item: the entry's line as <c>dictionary.txt</c> writes it.</summary>
/// <param name="Line">The line, with <c># off:</c> in front when the entry is switched off.</param>
public sealed record DictionaryLine([property: JsonPropertyName("line")] string Line);

/// <summary>The <c>data</c> of a <c>recording</c> item.</summary>
/// <param name="At">The history record's time, which names the file.</param>
public sealed record RecordingData([property: JsonPropertyName("at")] DateTimeOffset At);

/// <summary>What <c>sync-token.bin</c> holds once decrypted.</summary>
/// <param name="Token">The device token.</param>
/// <param name="Email">The account it belongs to.</param>
public sealed record SyncAccountData(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("email")] string Email);

/// <summary>
/// Source-generated JSON for everything sync writes, so it survives trimming. Compact and
/// with a fixed property order, because an item's hash is taken over exactly this output.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SyncState))]
[JsonSerializable(typeof(TranscriptRecord))]
[JsonSerializable(typeof(AppliedCorrection))]
[JsonSerializable(typeof(DictionarySuggestion))]
[JsonSerializable(typeof(SettingsData))]
[JsonSerializable(typeof(DictionaryLine))]
[JsonSerializable(typeof(RecordingData))]
[JsonSerializable(typeof(SyncAccountData))]
[JsonSerializable(typeof(Murmur.Abstractions.ManagedKeySet))]
public sealed partial class SyncJsonContext : JsonSerializerContext;
