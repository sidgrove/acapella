using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>A dictionary correction offered because the user made the same fix by hand.</summary>
public sealed record DictionarySuggestion
{
    /// <summary>Stable identity, for adding or dismissing one.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>What was typed, which becomes the trigger.</summary>
    public string Hear { get; init; } = string.Empty;

    /// <summary>What the user changed it to.</summary>
    public string Write { get; init; } = string.Empty;

    /// <summary>How many times the user has made this fix.</summary>
    public int Count { get; init; } = 1;

    /// <summary>When the fix was last seen.</summary>
    public DateTimeOffset LastSeen { get; init; }

    /// <summary>The words around the fix, as the user left them, so the suggestion reads in context.</summary>
    public string? Example { get; init; }

    /// <summary>Set when the user said no; the same fix is then never offered again.</summary>
    public bool Dismissed { get; init; }

    /// <summary>When the fix went into the dictionary on its own, without the user adding it; null until then.</summary>
    public DateTimeOffset? LearntAt { get; init; }

    /// <summary>Why it was added on its own, for the Dictionary tab and the log.</summary>
    public string? LearntBecause { get; init; }
}

/// <summary>
/// Which of the user's fixes are worth offering as dictionary corrections.
/// </summary>
/// <remarks>
/// Only swaps that sound alike: a mishearing is what a dictionary fixes, and a rewrite
/// ("want" to "need") is the user changing their mind, which no rule should repeat. Sounding
/// alike is judged on the letters, which catches the ones seen in Dave's history (Sarif and
/// serif, Gev and Jev, Cork Tax and Corp Tax, explanation mark and exclamation mark) and not
/// much else.
/// </remarks>
public static class DictionarySuggestions
{
    /// <summary>How alike two spellings must be, as one minus their edit distance over the longer length.</summary>
    public const double MinimumLikeness = 0.4;

    /// <summary>The most words either side of a fix that is still a mishearing rather than a rewrite.</summary>
    public const int MaximumWords = 3;

    /// <summary>The corrections to offer for <paramref name="changes"/>, leaving out any the dictionary already has.</summary>
    public static IReadOnlyList<(string Hear, string Write)> From(IReadOnlyList<WordChange> changes, IReadOnlyList<DictionaryEntry> entries)
    {
        var offers = new List<(string, string)>();
        foreach (var change in changes)
        {
            var hear = change.Typed.Trim();
            var write = change.Final.Trim();
            if (!IsWorthOffering(hear, write)) continue;
            if (entries.Any(e => e.Kind == EntryKind.Correction && string.Equals(e.Hear.Trim(), hear, StringComparison.OrdinalIgnoreCase))) continue;
            // "see" to "serif" would rewrite every "see"; a short trigger like "Gev" is fine
            // and the editor still warns about it if the user opens it.
            if (DictionaryWarning.IsOrdinaryWord(hear)) continue;
            offers.Add((hear, write));
        }
        return offers;
    }

    /// <summary>Whether a single fix looks like a mishearing put right.</summary>
    public static bool IsWorthOffering(string hear, string write)
    {
        if (hear.Length == 0 || write.Length == 0 || hear == write) return false;
        if (Count(hear) > MaximumWords || Count(write) > MaximumWords) return false;
        if (!write.Any(char.IsLetter)) return false;
        // Letters lost off one end ("And the Circle" to "ircle") are a read that began or
        // ended part-way through, not a fix anyone makes.
        var (heard, written) = (Letters(hear), Letters(write));
        if (written.Length < heard.Length && (heard.EndsWith(written, StringComparison.Ordinal) || heard.StartsWith(written, StringComparison.Ordinal))) return false;
        return IsCaseOnly(hear, write) || Likeness(heard, written) >= MinimumLikeness;
    }

    /// <summary>A few words either side of <paramref name="write"/> in <paramref name="final"/>, to show the fix in context.</summary>
    public static string? Example(string final, string write, int around = 50)
    {
        var at = final.IndexOf(write, StringComparison.Ordinal);
        if (at < 0) return null;
        var start = Math.Max(0, at - around);
        var end = Math.Min(final.Length, at + write.Length + around);
        // Whole words only at the cut ends.
        while (start > 0 && !char.IsWhiteSpace(final[start - 1])) start++;
        while (end < final.Length && !char.IsWhiteSpace(final[end])) end--;
        if (start > at) start = at;
        if (end < at + write.Length) end = at + write.Length;
        return (start > 0 ? "…" : string.Empty) + final[start..end].Trim() + (end < final.Length ? "…" : string.Empty);
    }

    private static bool IsCaseOnly(string hear, string write) =>
        !string.Equals(hear, write, StringComparison.Ordinal) && string.Equals(hear, write, StringComparison.OrdinalIgnoreCase);

    private static int Count(string phrase) => phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Letters(string text) => new([.. text.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    /// <summary>One minus the edit distance over the longer length: 1 for the same letters, 0 for nothing in common.</summary>
    public static double Likeness(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return 1 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
    }
}

/// <summary>
/// Suggestions waiting for a yes or a no, and the ones added on their own, kept in a small
/// JSON file beside the dictionary so every entry the app added itself can be seen and undone.
/// </summary>
public sealed class SuggestionStore
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private DictionarySuggestion[] _all = [];

    /// <summary>Opens (and creates if needed) the suggestions at <paramref name="path"/>.</summary>
    public SuggestionStore(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
            {
                _all = JsonSerializer.Deserialize(File.ReadAllText(path), SuggestionJsonContext.Default.DictionarySuggestionArray) ?? [];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"dictionary suggestions could not be read: {e.Message}");
        }
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "dictionary-suggestions.json");

    /// <summary>Suggestions not yet added or dismissed, most often made first.</summary>
    public IReadOnlyList<DictionarySuggestion> Pending =>
        [.. _all.Where(s => !s.Dismissed && s.LearntAt is null).OrderByDescending(s => s.Count).ThenByDescending(s => s.LastSeen)];

    /// <summary>Fixes added to the dictionary on their own and not undone, newest first.</summary>
    public IReadOnlyList<DictionarySuggestion> Learnt =>
        [.. _all.Where(s => !s.Dismissed && s.LearntAt is not null).OrderByDescending(s => s.LearntAt)];

    /// <summary>Raised whenever the suggestions change. Any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Offers a fix, or counts it again if it has been offered before, and returns it as it
    /// now stands. A dismissed fix stays dismissed and comes back with <see cref="DictionarySuggestion.Dismissed"/> set.
    /// </summary>
    public DictionarySuggestion Offer(string hear, string write, string? example, DateTimeOffset at)
    {
        DictionarySuggestion result;
        lock (_lock)
        {
            var index = Array.FindIndex(_all, s => string.Equals(s.Hear, hear, StringComparison.OrdinalIgnoreCase) && string.Equals(s.Write, write, StringComparison.Ordinal));
            if (index >= 0)
            {
                if (_all[index].Dismissed) return _all[index];
                result = _all[index] with { Count = _all[index].Count + 1, LastSeen = at, Example = example ?? _all[index].Example };
                _all = [.. _all[..index], result, .. _all[(index + 1)..]];
            }
            else
            {
                result = new DictionarySuggestion { Hear = hear, Write = write, LastSeen = at, Example = example };
                _all = [.. _all, result];
            }
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Records that a suggestion went into the dictionary on its own, and why.</summary>
    public void MarkLearnt(Guid id, DateTimeOffset at, string because)
    {
        lock (_lock)
        {
            _all = [.. _all.Select(s => s.Id == id ? s with { LearntAt = at, LearntBecause = because } : s)];
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remembers a no.</summary>
    public void Dismiss(Guid id)
    {
        lock (_lock)
        {
            _all = [.. _all.Select(s => s.Id == id ? s with { Dismissed = true } : s)];
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets a suggestion, once it has been added to the dictionary.</summary>
    public void Remove(Guid id)
    {
        lock (_lock)
        {
            _all = [.. _all.Where(s => s.Id != id)];
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_all, SuggestionJsonContext.Default.DictionarySuggestionArray));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"dictionary suggestions could not be saved: {e.Message}");
        }
    }
}

/// <summary>Source-generated JSON for the suggestions, which survives trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DictionarySuggestion[]))]
public sealed partial class SuggestionJsonContext : JsonSerializerContext;
