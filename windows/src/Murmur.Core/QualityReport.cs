using System.Globalization;
using System.Text;

namespace Murmur.Core;

/// <summary>
/// How good the dictations have been, measured against what the user did with them.
/// </summary>
/// <remarks>
/// <para>
/// Free and continuous: it reads only the history. A dictation the user edited is a
/// measured mistake, one they left alone after it was read back is a measured success, and
/// the ones never read back (sent at once, or an app that cannot be read) are counted but not
/// scored. Benchmarks did not transfer to Dave's voice on 24/09/2026; this is his own voice,
/// every day.
/// </para>
/// <para>
/// Speed is the history's processing time: transcription and clean-up once the recording is
/// in hand, before typing. The whole wait felt is the log's stop-to-complete, typically a
/// quarter of a second longer.
/// </para>
/// </remarks>
public static class QualityReport
{
    /// <summary>British dates and numbers: 25/09/2026, and "50%" rather than invariant "50 %".</summary>
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Builds the report over the <paramref name="days"/> days up to <paramref name="now"/>.</summary>
    public static string Build(IReadOnlyList<TranscriptRecord> records, DateTimeOffset now, int days = 7)
    {
        var since = now.AddDays(-days);
        var window = records.Where(r => r.At >= since && r.At <= now).OrderBy(r => r.At).ToList();
        var text = new StringBuilder();

        text.AppendLine(Uk, $"Acapella quality, {now.ToString("dd/MM/yyyy HH:mm", Uk)}, last {days} days");
        text.AppendLine();

        if (window.Count == 0)
        {
            text.AppendLine("No dictations in this period.");
            return text.ToString();
        }

        Summary(text, "All", window);
        text.AppendLine();

        text.AppendLine("By day");
        foreach (var day in window.GroupBy(r => r.At.ToLocalTime().Date))
        {
            Line(text, day.Key.ToString("ddd dd/MM", Uk), [.. day]);
        }
        text.AppendLine();

        text.AppendLine("By route");
        foreach (var route in window.GroupBy(Route).OrderByDescending(g => g.Count()))
        {
            Line(text, route.Key, [.. route]);
        }

        var fixes = window
            .Where(r => r.EditedText is not null)
            .SelectMany(r => EditAlignment.Changes(r.Text, r.EditedText!))
            .GroupBy(c => $"{c.Typed} -> {c.Final}", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .ToList();
        if (fixes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Words you fixed most");
            foreach (var fix in fixes) text.AppendLine(Uk, $"  {fix.Key}{(fix.Count() > 1 ? $"  ×{fix.Count()}" : string.Empty)}");
        }

        return text.ToString();
    }

    /// <summary>Share of checked dictations the user edited, and word error rate against their edits.</summary>
    public static (int Checked, int Edited, double WordErrorRate) Accuracy(IReadOnlyList<TranscriptRecord> records)
    {
        var checkedOnes = records.Where(r => r.EditChecked).ToList();
        if (checkedOnes.Count == 0) return (0, 0, 0);

        var edited = checkedOnes.Count(r => r.EditedText is not null);
        double errors = 0;
        double words = 0;
        foreach (var record in checkedOnes)
        {
            var final = record.EditedText ?? record.Text;
            var count = final.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            errors += record.EditedText is null ? 0 : EditAlignment.WordErrorRate(record.Text, final) * count;
            words += count;
        }
        return (checkedOnes.Count, edited, words == 0 ? 0 : errors / words);
    }

    private static string Route(TranscriptRecord record) =>
        $"{(record.TranscribedBy is null ? "local" : $"{record.TranscribedBy} + local")}, {(record.CleanupFailed ? "clean-up failed" : record.CleanedBy ?? "no clean-up")}";

    private static void Summary(StringBuilder text, string label, List<TranscriptRecord> records)
    {
        var (checkedCount, edited, wer) = Accuracy(records);
        var seconds = records.Select(r => r.ProcessingSeconds).Where(s => s > 0).Order().ToList();
        text.AppendLine(Uk, $"{label}: {records.Count} dictations");
        text.AppendLine(Uk, $"  Read back after typing: {checkedCount} ({Percent(checkedCount, records.Count)})");
        if (checkedCount > 0)
        {
            text.AppendLine(Uk, $"  Edited by you: {edited} of {checkedCount} ({Percent(edited, checkedCount)})");
            text.AppendLine(Uk, $"  Word error rate against what you left: {wer:P1}");
        }
        if (seconds.Count > 0)
        {
            text.AppendLine(Uk, $"  Transcribe and clean up, before typing: median {Quantile(seconds, 0.5):0.00} s, slowest tenth {Quantile(seconds, 0.9):0.00} s");
        }
    }

    private static void Line(StringBuilder text, string label, IReadOnlyList<TranscriptRecord> records)
    {
        var (checkedCount, edited, wer) = Accuracy(records);
        var seconds = records.Select(r => r.ProcessingSeconds).Where(s => s > 0).Order().ToList();
        text.Append(Uk, $"  {label,-40} {records.Count,4} dictations");
        if (checkedCount > 0) text.Append(Uk, $", {edited}/{checkedCount} edited, WER {wer:P1}");
        if (seconds.Count > 0) text.Append(Uk, $", median {Quantile(seconds, 0.5):0.00} s");
        text.AppendLine();
    }

    private static string Percent(int part, int whole) => whole == 0 ? "0%" : ((double)part / whole).ToString("P0", Uk);

    private static double Quantile(List<double> sorted, double q) => sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];
}
