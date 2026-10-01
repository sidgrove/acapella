using System.Text.RegularExpressions;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>
/// Puts back the user's own spellings when the AI clean-up undoes a dictionary correction.
/// </summary>
/// <remarks>
/// <para>
/// The dictionary runs before the clean-up, and the clean-up tidies what it is given back
/// towards ordinary English: "yeah -> yeh" fired on every "yeah" in late 09/2026 and Gemini
/// wrote "yeah" again every time, so the correction never reached the screen.
/// </para>
/// <para>
/// Only corrections that fired on this dictation, and only those written all in lower case:
/// a spelling the user prefers for an ordinary word. A capitalised target (a name, a product)
/// is left to the clean-up, which has the sentence to tell "reflect" the verb from Reflekt
/// the product; the corrector has already offered it.
/// </para>
/// </remarks>
public static class KeptCorrections
{
    private static readonly char[] PhraseSeparators = [' ', '-', '\t'];

    /// <summary><paramref name="cleaned"/> with each undone lower-case correction applied again.</summary>
    /// <param name="cleaned">What the clean-up returned.</param>
    /// <param name="applied">The corrections that fired before the clean-up.</param>
    /// <param name="entries">The dictionary.</param>
    public static string Apply(string cleaned, IReadOnlyList<AppliedCorrection> applied, IReadOnlyList<DictionaryEntry> entries)
    {
        if (applied.Count == 0 || string.IsNullOrEmpty(cleaned)) return cleaned;

        var result = cleaned;
        foreach (var entry in entries.Where(e => e.IsEnabled && e.Kind == EntryKind.Correction && IsSpelling(e.Write))
                     .Where(e => applied.Any(a => string.Equals(a.To, e.Write, StringComparison.Ordinal))))
        {
            if (Pattern(entry.Hear) is not { } pattern) continue;
            var write = entry.Write.Trim();
            // The corrector writes the target as listed; here a capital the clean-up gave the
            // word at a sentence start is kept, so "Yeah, fine" becomes "Yeh, fine".
            var text = result;
            result = pattern.Replace(text, m => char.IsUpper(m.Value[0]) && StartsSentence(text, m.Index) ? char.ToUpperInvariant(write[0]) + write[1..] : write);
        }
        return result;
    }

    private static bool StartsSentence(string text, int at)
    {
        var i = at - 1;
        while (i >= 0 && (char.IsWhiteSpace(text[i]) || text[i] is '"' or '“' or '(')) i--;
        return i < 0 || text[i] is '.' or '!' or '?' or '\n';
    }

    private static bool IsSpelling(string write) =>
        write.Any(char.IsLetter) && !write.Any(char.IsUpper);

    /// <summary>The same whole-word, glue-tolerant match as <see cref="DictionaryCorrector"/>.</summary>
    private static Regex? Pattern(string trigger)
    {
        var parts = trigger.Trim().Split(PhraseSeparators, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape).ToArray();
        if (parts.Length == 0) return null;
        try
        {
            return new Regex($@"(?<![\p{{L}}\p{{N}}]){string.Join(@"[\s\-]*", parts)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
