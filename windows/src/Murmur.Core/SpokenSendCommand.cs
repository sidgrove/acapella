using System.Text.RegularExpressions;

namespace Murmur.Core;

/// <summary>Separates an explicit terminal send command from dictated content.</summary>
public static class SpokenSendCommand
{
    /// <summary>
    /// What the speech model tends to hear when someone says "send it" on its own. Mined
    /// from Dave's history on 2026-09-14: 17 standalone attempts came back as these and were
    /// typed into the chat instead of sending. Used only when the whole utterance is one of
    /// them; a word like "Sunday" inside a sentence is left alone.
    /// </summary>
    public const string DefaultSendOnlyAliases = "sender, sander, sanda, sendit, send a, send the, send that, sent it, sunday";

    /// <summary>
    /// Recognises a command only when it is the entire utterance, apart from punctuation.
    /// </summary>
    /// <param name="text">The raw transcript.</param>
    /// <param name="phrase">The configured phrase, e.g. "send it".</param>
    /// <param name="aliases">Comma-separated mishearings that count as the phrase when spoken alone.</param>
    public static bool IsStandalone(string text, string? phrase, string? aliases = null)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        var pattern = @"\A\s*" + Alternatives(phrase, aliases) + @"[\s.!?,;:]*\z";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }

    private static string Alternatives(string word, string? aliases)
    {
        var words = (aliases ?? string.Empty).Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Append(word.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => candidate.Length).Select(Regex.Escape);
        return "(?:" + string.Join('|', words) + ")";
    }
    /// <summary>Matches the send word or a configured alternative only at the end of dictation.</summary>
    public static (string Text, bool Send) Extract(string text, string? sendWord, string? aliases = null)
    {
        if (string.IsNullOrWhiteSpace(sendWord)) return (text, false);
        var pattern = @"(?<![\p{L}\p{N}_])" + Alternatives(sendWord, aliases) + @"[\s.!?,;:]*\z";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        if (!match.Success) return (text, false);
        return (text[..match.Index].TrimEnd(' ', '\t', '\r', '\n', ',', ';', ':', '-', '—', '–'), true);
    }
}