using System.Text.Json;
using Murmur.Dictionary;

namespace Murmur.Core.Sync;

/// <summary>One item as this PC holds it now: its key and its canonical JSON.</summary>
/// <param name="Kind">The sync kind.</param>
/// <param name="Key">The key within the kind.</param>
/// <param name="Json">The canonical JSON the hash is taken over and the <c>data</c> sent.</param>
public sealed record SyncItem(string Kind, string Key, string Json)
{
    /// <summary>The state key, <c>kind|key</c>.</summary>
    public string Id => SyncState.Id(Kind, Key);

    /// <summary>The hash of <see cref="Json"/>.</summary>
    public string Hash { get; } = SyncState.Hash(Json);
}

/// <summary>
/// One kind of thing that syncs: how to list it, how to read a pulled copy into the same
/// canonical form, and how to apply a page of pulled changes through the store's own API.
/// </summary>
internal interface ISyncKind
{
    string Kind { get; }

    IEnumerable<SyncItem> Snapshot();

    /// <summary>The canonical JSON for a pulled row, or null when it is not a valid item for <paramref name="key"/>.</summary>
    string? Canonical(string key, JsonElement data);

    /// <summary>Applies a page's upserts (key, canonical JSON) and removals in one go.</summary>
    void Apply(IReadOnlyList<(string Key, string Json)> upserts, IReadOnlyList<string> removals);
}

/// <summary>The names of the kinds on the wire.</summary>
public static class SyncKinds
{
    /// <summary>Dictionary entries.</summary>
    public const string Dictionary = "dictionary";

    /// <summary>Dictionary suggestions, learnt fixes included.</summary>
    public const string Suggestion = "suggestion";

    /// <summary>History records.</summary>
    public const string History = "history";

    /// <summary>The synced settings, under one key.</summary>
    public const string Settings = "settings";

    /// <summary>A corrected recording that exists on some PC.</summary>
    public const string Recording = "recording";

    /// <summary>The one key the settings sync under.</summary>
    public const string SettingsKey = "preferences";

    /// <summary>
    /// A dictionary entry's key: <c>Term|{write}</c> or <c>Correction|{hear lower-cased}|{write}</c>.
    /// The on/off flag is in the line, not the key, so switching an entry off is an edit.
    /// </summary>
    public static string DictionaryKey(DictionaryEntry entry) => entry.Kind == EntryKind.Correction
        ? $"Correction|{entry.Hear.Trim().ToLowerInvariant()}|{entry.Write.Trim()}"
        : $"Term|{entry.Write.Trim()}";
}

internal sealed class DictionaryKind(DictionaryFile file) : ISyncKind
{
    public string Kind => SyncKinds.Dictionary;

    public IEnumerable<SyncItem> Snapshot()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in file.Entries)
        {
            var key = SyncKinds.DictionaryKey(entry);
            // The same entry twice in the file: the first one is the one that syncs.
            if (!seen.Add(key)) continue;
            yield return new SyncItem(Kind, key, Json(Normalised(entry)));
        }
    }

    public string? Canonical(string key, JsonElement data)
    {
        var entry = Read(data);
        return entry is not null && SyncKinds.DictionaryKey(entry) == key ? Json(entry) : null;
    }

    public void Apply(IReadOnlyList<(string Key, string Json)> upserts, IReadOnlyList<string> removals)
    {
        var incoming = new Dictionary<string, DictionaryEntry>(StringComparer.Ordinal);
        foreach (var (key, json) in upserts)
        {
            if (FromLine(JsonSerializer.Deserialize(json, SyncJsonContext.Default.DictionaryLine)?.Line) is { } entry) incoming[key] = entry;
        }
        var gone = removals.ToHashSet(StringComparer.Ordinal);

        file.Edit(entries =>
        {
            var result = new List<DictionaryEntry>(entries.Count + incoming.Count);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var key = SyncKinds.DictionaryKey(entry);
                if (gone.Contains(key)) continue;
                if (incoming.TryGetValue(key, out var replacement))
                {
                    // In place, keeping the id the editor may be holding; any duplicate goes.
                    if (placed.Add(key)) result.Add(replacement with { Id = entry.Id });
                    continue;
                }
                result.Add(entry);
            }
            foreach (var (key, entry) in incoming)
            {
                if (!placed.Contains(key)) result.Add(entry);
            }
            return result;
        });
    }

    private static DictionaryEntry Normalised(DictionaryEntry entry) =>
        entry with { Write = entry.Write.Trim(), Hear = entry.Hear.Trim() };

    private static string Json(DictionaryEntry entry) =>
        JsonSerializer.Serialize(new DictionaryLine(entry.ToFileLine()), SyncJsonContext.Default.DictionaryLine);

    private static DictionaryEntry? Read(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("line", out var line) || line.ValueKind != JsonValueKind.String) return null;
        return FromLine(line.GetString());
    }

    private static DictionaryEntry? FromLine(string? line)
    {
        var parsed = DictionaryFile.Parse(line ?? string.Empty);
        return parsed.Count == 1 ? parsed[0] : null;
    }
}

internal sealed class SuggestionKind(SuggestionStore store) : ISyncKind
{
    public string Kind => SyncKinds.Suggestion;

    public IEnumerable<SyncItem> Snapshot() =>
        store.All.DistinctBy(s => s.Id).Select(s => new SyncItem(Kind, s.Id.ToString("D"), Json(s)));

    public string? Canonical(string key, JsonElement data)
    {
        var suggestion = Read(data);
        return suggestion is not null && suggestion.Id.ToString("D") == key ? Json(suggestion) : null;
    }

    public void Apply(IReadOnlyList<(string Key, string Json)> upserts, IReadOnlyList<string> removals) =>
        store.Merge(
            upserts.Select(u => JsonSerializer.Deserialize(u.Json, SyncJsonContext.Default.DictionarySuggestion)).OfType<DictionarySuggestion>().ToList(),
            removals.Select(k => Guid.TryParse(k, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList());

    private static string Json(DictionarySuggestion suggestion) =>
        JsonSerializer.Serialize(suggestion, SyncJsonContext.Default.DictionarySuggestion);

    private static DictionarySuggestion? Read(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        try
        {
            return data.Deserialize(SyncJsonContext.Default.DictionarySuggestion);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class HistoryKind(TranscriptStore store) : ISyncKind
{
    public string Kind => SyncKinds.History;

    public IEnumerable<SyncItem> Snapshot() =>
        store.Records.DistinctBy(r => r.Id).Select(r => new SyncItem(Kind, r.Id.ToString("D"), Json(r)));

    public string? Canonical(string key, JsonElement data)
    {
        var record = Read(data);
        return record is not null && record.Id.ToString("D") == key ? Json(record) : null;
    }

    public void Apply(IReadOnlyList<(string Key, string Json)> upserts, IReadOnlyList<string> removals) =>
        store.Merge(
            upserts.Select(u => JsonSerializer.Deserialize(u.Json, SyncJsonContext.Default.TranscriptRecord)).OfType<TranscriptRecord>().ToList(),
            removals.Select(k => Guid.TryParse(k, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList());

    internal static string Json(TranscriptRecord record) =>
        JsonSerializer.Serialize(record, SyncJsonContext.Default.TranscriptRecord);

    private static TranscriptRecord? Read(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        try
        {
            return data.Deserialize(SyncJsonContext.Default.TranscriptRecord);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class SettingsKind(AppSettings settings) : ISyncKind
{
    public string Kind => SyncKinds.Settings;

    public IEnumerable<SyncItem> Snapshot() => [new SyncItem(Kind, SyncKinds.SettingsKey, SyncedSettings.Canonical(settings.Data))];

    public string? Canonical(string key, JsonElement data) =>
        key == SyncKinds.SettingsKey && SyncedSettings.Read(data) is { } incoming ? SyncedSettings.Canonical(incoming) : null;

    public void Apply(IReadOnlyList<(string Key, string Json)> upserts, IReadOnlyList<string> removals)
    {
        // Settings cannot be deleted; a tombstone for them is ignored.
        if (upserts.Count == 0) return;
        if (JsonSerializer.Deserialize(upserts[^1].Json, SyncJsonContext.Default.SettingsData) is not { } incoming) return;
        settings.Update(SyncedSettings.Merge(incoming, settings.Data));
    }
}
