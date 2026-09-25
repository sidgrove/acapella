using System.Globalization;
using System.Text;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Speech;

namespace Murmur.App;

/// <summary>
/// <c>--report</c> and <c>--compare</c>: the quality of recent dictations, and how other
/// clean-up models would have done on the same ones.
/// </summary>
/// <remarks>
/// Both write to the console and to a file in the data folder, because the app is a windowed
/// exe and a console launched by hand does not always show its output.
/// </remarks>
internal static class QualityCommands
{
    /// <summary>British dates and numbers in what is printed.</summary>
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Prices per million tokens, input then output, as of 25/09/2026. Anything unknown is priced as 2.5 Flash.</summary>
    private static readonly Dictionary<string, (double In, double Out)> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemini-2.5-flash"] = (0.30, 2.50),
        ["gemini-2.5-flash-lite"] = (0.10, 0.40),
        ["gemini-3.5-flash-lite"] = (0.30, 2.50),
    };

    /// <summary>Handles the command if <paramref name="args"/> holds one. Null when it does not.</summary>
    public static int? TryRun(string[] args)
    {
        if (args.Contains("--report", StringComparer.OrdinalIgnoreCase)) return Report(Days(args));

        var compare = Array.FindIndex(args, a => string.Equals(a, "--compare", StringComparison.OrdinalIgnoreCase));
        if (compare >= 0)
        {
            var models = compare + 1 < args.Length && !args[compare + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[compare + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [GeminiCleaner.DefaultModel, "gemini-2.5-flash-lite"];
            var yes = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);
            return CompareAsync(models, Days(args), Limit(args), yes).GetAwaiter().GetResult();
        }

        return null;
    }

    private static int Days(string[] args) => Number(args, "--days") ?? 7;

    private static int Limit(string[] args) => Number(args, "--limit") ?? 100;

    private static int? Number(string[] args, string name)
    {
        var at = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }

    private static int Report(int days)
    {
        var transcripts = new TranscriptStore(TranscriptStore.DefaultPath);
        return Emit("quality-report.txt", QualityReport.Build(transcripts.Records, DateTimeOffset.Now, days));
    }

    /// <summary>
    /// Re-cleans recent dictations the user read back with each model, through the same
    /// dictionary, prompt and polish as a live dictation, and scores each against the words
    /// the user left. Without <c>--yes</c> it only says what that would cost.
    /// </summary>
    private static async Task<int> CompareAsync(string[] models, int days, int limit, bool yes)
    {
        var settings = new AppSettings(AppSettings.DefaultPath);
        var dictionary = new DictionaryFile(DictionaryFile.DefaultPath);
        var transcripts = new TranscriptStore(TranscriptStore.DefaultPath);
        var since = DateTimeOffset.Now.AddDays(-days);

        // Edited ones first: they are where the models can differ in a way that matters.
        var cases = transcripts.Records
            .Where(r => r.At >= since && r.EditChecked && r.RawText is { Length: > 0 })
            .OrderByDescending(r => r.EditedText is not null)
            .ThenByDescending(r => r.At)
            .Take(limit)
            .ToList();

        var output = new StringBuilder();
        output.AppendLine(Uk, $"Clean-up comparison, {DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm", Uk)}");
        if (cases.Count == 0)
        {
            output.AppendLine(Uk, $"No dictations read back after typing in the last {days} days yet. Leave \"Learn from my edits\" on for a few days first.");
            return Emit("quality-compare.txt", output.ToString());
        }

        var prompt = GeminiCleaner.Prompt(settings.Data.CustomInstructions, DictionaryCorrector.BiasPhrases(dictionary.Entries));
        var inputChars = cases.Sum(c => prompt.Length + (c.RawText?.Length ?? 0) + (c.LocalRawText?.Length ?? 0) + 400);
        var outputChars = cases.Sum(c => (c.EditedText ?? c.Text).Length);
        var cost = models.Sum(m =>
        {
            var (priceIn, priceOut) = Prices.TryGetValue(m, out var known) ? known : Prices[GeminiCleaner.DefaultModel];
            return inputChars / 4.0 / 1e6 * priceIn + outputChars / 4.0 / 1e6 * priceOut;
        });

        output.AppendLine(Uk, $"{cases.Count} dictations ({cases.Count(c => c.EditedText is not null)} you edited) × {models.Length} models = {cases.Count * models.Length} Gemini calls, about ${cost:0.000} on the app's own key.");
        if (!yes)
        {
            output.AppendLine("Nothing was sent. Run again with --yes to spend it.");
            return Emit("quality-compare.txt", output.ToString());
        }

        output.AppendLine();
        foreach (var model in models)
        {
            using var cleaner = new GeminiCleaner(() => settings.Data.GeminiApiKey, model,
                customInstructions: () => settings.Data.CustomInstructions,
                vocabulary: () => DictionaryCorrector.BiasPhrases(dictionary.Entries));

            double errors = 0;
            double words = 0;
            var failed = 0;
            var times = new List<double>();
            foreach (var record in cases)
            {
                var gold = record.EditedText ?? record.Text;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var typed = await CleanLikeLiveAsync(cleaner, record, settings.Data, dictionary.Entries).ConfigureAwait(false);
                times.Add(clock.Elapsed.TotalSeconds);
                if (typed is null)
                {
                    failed++;
                    if (cleaner.LastError is { } error && (error.Contains("429", StringComparison.Ordinal) || error.Contains("spend", StringComparison.OrdinalIgnoreCase)))
                    {
                        output.AppendLine(Uk, $"  {model}: stopped, the API said {error}");
                        break;
                    }
                    continue;
                }
                var count = gold.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                errors += EditAlignment.WordErrorRate(typed, gold) * count;
                words += count;

                // Paced, so a comparison never looks like a burst to the project's limits.
                await Task.Delay(150).ConfigureAwait(false);
            }

            times.Sort();
            var median = times.Count == 0 ? 0 : times[times.Count / 2];
            output.AppendLine(Uk, $"  {model,-28} WER {(words == 0 ? 0 : errors / words):P1}   median {median:0.00} s per call   {failed} failed");
        }

        output.AppendLine();
        output.AppendLine("WER is against the words you left in the field. The model that produced them has a head start on the ones you did not edit, so read the edited-only share with that in mind.");
        return Emit("quality-compare.txt", output.ToString());
    }

    /// <summary>The live path after transcription: dictionary, rules, clean-up, polish. The screen was not kept, so it is not shown.</summary>
    private static async Task<string?> CleanLikeLiveAsync(GeminiCleaner cleaner, TranscriptRecord record, SettingsData settings, IReadOnlyList<DictionaryEntry> entries)
    {
        var raw = record.RawText!;
        var forCleaner = SpokenFormatting.Apply(new DictionaryCorrector(entries).Apply(raw).Text, commands: false, settings.RemoveFillers);
        var local = record.LocalRawText is { Length: > 0 } heard
            ? SpokenFormatting.Apply(new DictionaryCorrector(entries).Apply(heard).Text, commands: false, settings.RemoveFillers)
            : null;

        var cleaned = local is not null
            ? await cleaner.CleanTwoReadingsAsync(forCleaner, local, CancellationToken.None).ConfigureAwait(false)
            : await cleaner.CleanAsync(forCleaner, CancellationToken.None).ConfigureAwait(false);
        if (cleaned is null) return null;

        if (settings.NoCommaBeforeAnd) cleaned = TranscriptPolish.RemoveCommaBeforeAnd(cleaned);
        if (settings.BritishSpelling) cleaned = BritishSpellings.Apply(cleaned);
        return TranscriptPolish.Apply(cleaned, settings.FullStops);
    }

    private static int Emit(string fileName, string text)
    {
        Console.Write(text);
        var path = Path.Combine(AppPaths.Root, fileName);
        try
        {
            File.WriteAllText(path, text);
            Console.WriteLine($"(saved to {path})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"(could not save to {path}: {e.Message})");
        }
        return 0;
    }
}
