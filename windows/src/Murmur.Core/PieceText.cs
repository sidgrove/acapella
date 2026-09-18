namespace Murmur.Core;

/// <summary>
/// Joining and splitting the text of a long dictation that is cleaned in pieces while it
/// is still being spoken.
/// </summary>
/// <remarks>
/// <para>
/// The audio is cut at the quietest moment in a window, which is a fine place to split
/// sound and a poor place to split text. The speech model closes every piece with a full
/// stop and opens the next with a capital, whether or not a sentence ended there, and the
/// clean-up of a piece runs before its successor exists. Seven days of history showed the
/// result in half of all long dictations: "management pack section. whereby we've got".
/// </para>
/// <para>
/// The cleaner is told a piece may stop mid-sentence and decides itself; the continuation
/// is cleaned with the earlier text as context and starts lower-case when the sentence
/// carries on. <see cref="JoinCleaned"/> is the deterministic safety net for the stop the
/// context still holds.
/// </para>
/// </remarks>
public static class PieceText
{
    /// <summary>Dotted abbreviations whose full stop does not end a sentence.</summary>
    private static readonly string[] Abbreviations = ["e.g.", "i.e.", "etc.", "vs.", "Ltd.", "Inc.", "No.", "Mr.", "Mrs.", "Dr.", "St."];

    /// <summary>
    /// Joins two cleaned pieces. A single full stop at the end of <paramref name="first"/>
    /// is dropped when <paramref name="second"/> carries the sentence on in lower case; an
    /// ellipsis or an abbreviation is left alone.
    /// </summary>
    public static string JoinCleaned(string first, string second)
    {
        if (first.Length == 0) return second;
        if (second.Length == 0) return first;

        var head = first.TrimEnd();
        var tail = second.TrimStart();
        if (tail.Length == 0) return first;

        if (head.Length >= 2 && head[^1] == '.' && char.IsLetter(head[^2]) && char.IsLower(tail[0]) && !EndsWithAbbreviation(head))
        {
            head = head[..^1];
        }

        return head + " " + tail;
    }

    /// <summary>
    /// Whether <paramref name="raw"/> ends in the full stop the speech model adds to any
    /// audio that simply stops, as opposed to a mark that carries meaning.
    /// </summary>
    public static bool EndsWithArtificialStop(string raw)
    {
        var trimmed = raw.TrimEnd();
        return trimmed.Length >= 2 && trimmed[^1] == '.' && char.IsLetterOrDigit(trimmed[^2]) && !EndsWithAbbreviation(trimmed);
    }

    /// <summary>Removes the artificial stop, if there is one, so the cleaner decides whether the sentence ended.</summary>
    public static string WithoutArtificialStop(string raw) =>
        EndsWithArtificialStop(raw) ? raw.TrimEnd()[..^1] : raw;

    private static bool EndsWithAbbreviation(string text)
    {
        foreach (var abbreviation in Abbreviations)
        {
            if (!text.EndsWith(abbreviation, StringComparison.OrdinalIgnoreCase)) continue;
            var start = text.Length - abbreviation.Length;
            if (start == 0 || !char.IsLetterOrDigit(text[start - 1])) return true;
        }
        return false;
    }
}
