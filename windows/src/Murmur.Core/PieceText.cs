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
        var tail = second.TrimStart();
        var opensSentence = OpensSentence(tail);
        if (opensSentence) tail = Capitalise(tail[1..].TrimStart());

        if (first.Length == 0) return opensSentence ? tail : second;
        if (tail.Length == 0) return first;
        var head = first.TrimEnd();
        if (head.Length == 0) return tail;

        // The cleaner of the continuation said a new sentence starts at the join. It is the
        // only one that can: "I", "I'm" and a lower-case continuation say nothing either way,
        // and on 2026-09-22 "hierarchy I feel like" and "amend just bear in mind" ran on.
        if (opensSentence)
        {
            if (head[^1] is ',' or ';' or ':') head = head[..^1] + ".";
            else if (head[^1] is not ('.' or '?' or '!')) head += ".";
            return head + " " + tail;
        }

        if (head.Length >= 2 && head[^1] == '.' && char.IsLetter(head[^2]) && EndsWithDanglingWord(head))
        {
            // No sentence ends on "of" or "the", so a stop there is the cut's, whatever the
            // continuation's case: "making the most effective use of. Jev and Gen AI".
            head = head[..^1];
            tail = LowerCaseIfOrdinary(tail);
        }
        else if (head.Length >= 2 && head[^1] == '.' && char.IsLetter(head[^2]) && char.IsLower(tail[0]) && !EndsWithAbbreviation(head))
        {
            head = head[..^1];
        }
        // The converse: the piece was cut exactly at a sentence end, its stop was taken as
        // artificial, and the cleaner then opened the continuation with a capital. Seen
        // on 2026-09-21 as "the groups mapping And also" and "industry Keep it". A
        // capital "I", an acronym or a name in capitals says nothing about sentences;
        // only an ordinary capitalised word counts.
        else if (char.IsLetterOrDigit(head[^1]) && StartsNewSentence(tail))
        {
            head += ".";
        }
        // A question or exclamation the earlier piece's cleaner kept is real punctuation,
        // so whatever follows opens a sentence even though the continuation's cleaner,
        // told the sentence might carry on, wrote it in lower case: "that exist? in the
        // system" on 2026-09-24.
        else if (head[^1] is '?' or '!' && char.IsLower(tail[0]))
        {
            tail = Capitalise(tail);
        }

        return head + " " + tail;
    }

    private static bool StartsNewSentence(string text) =>
        text.Length >= 2 && char.IsUpper(text[0]) && char.IsLower(text[1]) && !(text[0] == 'I' && text[1] == '\'');

    /// <summary>
    /// The mark the cleaner of a continuation opens its reply with when a new sentence starts
    /// at the join: a single full stop, never an ellipsis.
    /// </summary>
    public const string NewSentenceMark = ". ";

    /// <summary>Whether a cleaned continuation opens with <see cref="NewSentenceMark"/>.</summary>
    public static bool OpensSentence(string cleaned) =>
        cleaned.Length >= 1 && cleaned[0] == '.' && (cleaned.Length == 1 || cleaned[1] != '.');

    /// <summary>A cleaned continuation without <see cref="NewSentenceMark"/>, for checks that count words.</summary>
    public static string WithoutNewSentenceMark(string cleaned)
    {
        var trimmed = cleaned.TrimStart();
        return OpensSentence(trimmed) ? trimmed[1..].TrimStart() : cleaned;
    }

    /// <summary>
    /// Words no sentence ends on, so a full stop after one was put there by the cut. Kept
    /// short on purpose: "log in.", "that's what it is." and "plan A." all end sentences.
    /// </summary>
    private static readonly HashSet<string> DanglingWords = new(StringComparer.Ordinal)
    {
        "an", "the", "of", "into", "onto", "and", "or", "but", "nor", "than",
        "because", "whereas", "whether", "whereby", "my", "your", "our", "their",
    };

    /// <summary>Opening words that are only capitalised because a sentence was taken to start.</summary>
    private static readonly HashSet<string> OrdinaryOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "these", "those", "it", "its", "my", "your", "our", "their",
        "his", "her", "what", "which", "how", "some", "any", "all", "each", "every", "more", "most",
    };

    private static bool EndsWithDanglingWord(string text)
    {
        var body = text[..^1];
        var start = body.Length;
        while (start > 0 && char.IsLetter(body[start - 1])) start--;
        return DanglingWords.Contains(body[start..]);
    }

    private static string LowerCaseIfOrdinary(string text)
    {
        var end = 0;
        while (end < text.Length && char.IsLetter(text[end])) end++;
        return end > 0 && OrdinaryOpeners.Contains(text[..end]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;
    }

    private static string Capitalise(string text) =>
        text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;

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
