using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Core;

/// <summary>
/// One dictation as a training example: what was heard, what was typed and what the user
/// settled on, with the audio when it was kept.
/// </summary>
/// <remarks>
/// The makings of a model tuned to one person. Heard to Final pairs teach a clean-up model
/// the user's corrections and habits; Audio to Final pairs teach a speech model their voice.
/// Only examples the user read back (<see cref="Checked"/>) have a trustworthy
/// <see cref="Final"/>.
/// </remarks>
public sealed record TrainingExample
{
    /// <summary>When the key was released; also names the recording.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>The app it went into, when known.</summary>
    public string? App { get; init; }

    /// <summary>The kind of writing the clean-up was told it was, when known.</summary>
    public string? Style { get; init; }

    /// <summary>The recording, when it is still on this PC.</summary>
    public string? Audio { get; init; }

    /// <summary>What the speech model heard: the cloud's words when there were two readings.</summary>
    public string Heard { get; init; } = string.Empty;

    /// <summary>What the local model heard, beside a cloud reading.</summary>
    public string? HeardLocally { get; init; }

    /// <summary>What was typed.</summary>
    public string Typed { get; init; } = string.Empty;

    /// <summary>What the user left in the field; null when it was never read back.</summary>
    public string? Final { get; init; }

    /// <summary>Whether the field was read back, so <see cref="Final"/> can be trusted.</summary>
    public bool Checked { get; init; }

    /// <summary>Whether the user changed any words.</summary>
    public bool Edited { get; init; }
}

/// <summary>Builds the training set from the history, all on this PC.</summary>
public static class TrainingSet
{
    /// <summary>Every history record with a raw transcript, oldest first.</summary>
    /// <param name="records">The history.</param>
    /// <param name="audioFor">Finds a dictation's recording, or null.</param>
    public static IReadOnlyList<TrainingExample> From(IEnumerable<TranscriptRecord> records, Func<DateTimeOffset, string?> audioFor) =>
        [.. records
            .Where(r => r.RawText is { Length: > 0 })
            .OrderBy(r => r.At)
            .Select(r => new TrainingExample
            {
                At = r.At,
                App = r.App,
                Style = r.Style,
                Audio = audioFor(r.At),
                Heard = r.RawText!,
                HeardLocally = r.LocalRawText,
                Typed = r.Text,
                Final = r.EditChecked ? r.EditedText ?? r.Text : null,
                Checked = r.EditChecked,
                Edited = r.EditedText is not null,
            })];

    /// <summary>One JSON object per line.</summary>
    public static string ToJsonLines(IEnumerable<TrainingExample> examples)
    {
        var text = new StringBuilder();
        foreach (var example in examples) text.Append(JsonSerializer.Serialize(example, TrainingJsonContext.Default.TrainingExample)).Append('\n');
        return text.ToString();
    }

    /// <summary>A few lines on what the set holds, for the console.</summary>
    public static string Summary(IReadOnlyList<TrainingExample> examples)
    {
        var checkedCount = examples.Count(e => e.Checked);
        var edited = examples.Count(e => e.Edited);
        var audio = examples.Count(e => e.Audio is not null);
        var editedAudio = examples.Count(e => e.Edited && e.Audio is not null);
        return $"{examples.Count} dictations: {checkedCount} read back after typing, {edited} of them edited by you.\n"
             + $"{audio} still have their recording, {editedAudio} of those edited.";
    }
}

/// <summary>Source-generated JSON for the training set.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TrainingExample))]
public sealed partial class TrainingJsonContext : JsonSerializerContext;
