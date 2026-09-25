using Murmur.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>One saved dictation.</summary>
public sealed record TranscriptRecord
{
    /// <summary>Stable identity, so one entry can be deleted without matching on its text.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>When the key was released.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>How long the key was held.</summary>
    public double AudioSeconds { get; init; }

    /// <summary>Release to finished text — the wait actually felt.</summary>
    public double ProcessingSeconds { get; init; }

    /// <summary>The final text, after corrections.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Corrections that fired, if any.</summary>
    public IReadOnlyList<AppliedCorrection>? Corrections { get; init; }

    /// <summary>The model that cleaned the text, or null if the raw transcript was typed.</summary>
    public string? CleanedBy { get; init; }

    /// <summary>What the speech model heard, before rules and clean-up. Null in older records.</summary>
    public string? RawText { get; init; }

    /// <summary>The AI tier was on but its answer was unusable, so the local text was typed.</summary>
    public bool CleanupFailed { get; init; }

    /// <summary>The cloud model whose words <see cref="RawText"/> are, or null for the local model's.</summary>
    public string? TranscribedBy { get; init; }

    /// <summary>What the local model heard, kept beside a cloud transcript for comparison.</summary>
    public string? LocalRawText { get; init; }

    /// <summary>
    /// Whether the field was read back after typing, so <see cref="EditedText"/> being null
    /// means the user left the words alone rather than that nobody looked.
    /// </summary>
    public bool EditChecked { get; init; }

    /// <summary>The words as the user left them in the field, when they changed any. The truest record of what should have been typed.</summary>
    public string? EditedText { get; init; }

    /// <summary>The app it was typed into, e.g. "claude" or "slack", when that was read.</summary>
    public string? App { get; init; }

    /// <summary>The kind of writing the clean-up was told it was ("Email", "Chat", "Prompt", "Document"), or null.</summary>
    public string? Style { get; init; }
}

/// <summary>
/// Transcript history, appended to a JSONL file.
/// </summary>
/// <remarks>
/// <para>
/// One JSON object per line rather than one big array: appending a line is cheap and a
/// truncated write costs one record rather than the whole file. Deleting requires a rewrite,
/// which is fine — it is rare and the file is small.
/// </para>
/// <para>
/// Serialization is source-generated (<see cref="TranscriptJsonContext"/>) so this survives
/// trimming and single-file publishing, where reflection-based JSON quietly stops working.
/// </para>
/// </remarks>
public sealed class TranscriptStore
{
    private readonly string _path;
    private readonly Lock _lock = new();

    /// <summary>
    /// Copy-on-write, newest first. <see cref="Add"/> runs on the engine's thread as a
    /// dictation completes while the list view enumerates <see cref="Records"/> on the UI
    /// thread; replacing the array whole means a reader always sees a consistent list.
    /// </summary>
    private TranscriptRecord[] _records = [];

    /// <summary>Opens (and creates if needed) the history at <paramref name="path"/>.</summary>
    public TranscriptStore(string path)
    {
        _path = path;
        Reload();
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "transcripts.jsonl");

    /// <summary>Every record, newest first.</summary>
    public IReadOnlyList<TranscriptRecord> Records => _records;

    /// <summary>Raised whenever the history changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Re-reads the file.</summary>
    public void Reload()
    {
        var loaded = new List<TranscriptRecord>();

        try
        {
            if (File.Exists(_path))
            {
                foreach (var line in File.ReadLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // A single corrupt line must not destroy the whole history — skip it and
                    // keep everything else.
                    try
                    {
                        var record = JsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptRecord);
                        if (record is not null) loaded.Add(record);
                    }
                    catch (JsonException)
                    {
                        // Skip.
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"history could not be read: {e.Message}");
        }

        loaded.Reverse();   // newest first, which is how the list reads
        lock (_lock) _records = [.. loaded];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Appends a record.</summary>
    public void Add(TranscriptRecord record)
    {
        var line = JsonSerializer.Serialize(record, TranscriptJsonContext.Default.TranscriptRecord);
        lock (_lock)
        {
            _records = [record, .. _records];
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"history could not be saved: {e.Message}");
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces the newest record that <paramref name="match"/> picks with what
    /// <paramref name="change"/> makes of it. False when none matched.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="Updated"/>, not <see cref="Changed"/>: the list on screen does not
    /// show what this touches, and rebuilding every card a minute after each dictation would
    /// be the slow history refresh of 18/09 all over again.
    /// </remarks>
    public bool Update(Func<TranscriptRecord, bool> match, Func<TranscriptRecord, TranscriptRecord> change)
    {
        lock (_lock)
        {
            var index = Array.FindIndex(_records, r => match(r));
            if (index < 0) return false;
            var records = (TranscriptRecord[])_records.Clone();
            records[index] = change(records[index]);
            _records = records;
            Rewrite();
        }
        Updated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Raised when a record changes in place, for fields the list does not show.</summary>
    public event EventHandler? Updated;

    /// <summary>Deletes one record.</summary>
    public void Remove(Guid id)
    {
        lock (_lock)
        {
            _records = _records.Where(r => r.Id != id).ToArray();
            Rewrite();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes everything.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _records = [];
            Rewrite();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Case-insensitive search over transcript text.</summary>
    public IReadOnlyList<TranscriptRecord> Search(string query)
    {
        var records = _records;
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return records;

        return records
            .Where(r => r.Text.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                     || (r.RawText?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    /// <summary>Writes the whole list. Call under <see cref="_lock"/>.</summary>
    private void Rewrite()
    {
        // Oldest first on disk, so a plain append stays correct next time.
        var lines = _records
            .Reverse()
            .Select(r => JsonSerializer.Serialize(r, TranscriptJsonContext.Default.TranscriptRecord));

        try
        {
            AtomicFile.WriteAllLines(_path, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"history could not be saved: {e.Message}");
        }
    }
}

/// <summary>
/// Source-generated JSON for the transcript store.
/// </summary>
/// <remarks>
/// Reflection-based serialization breaks under trimming and is flagged by the single-file
/// analyzer. Generating it keeps the published binary honest.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(TranscriptRecord))]
[JsonSerializable(typeof(AppliedCorrection))]
public sealed partial class TranscriptJsonContext : JsonSerializerContext;
