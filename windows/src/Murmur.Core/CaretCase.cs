namespace Murmur.Core;

/// <summary>
/// Drops the capital from the first word of a dictation that starts part way through a
/// sentence.
/// </summary>
/// <remarks>
/// <para>
/// The speech model and the clean-up both capitalise the start of every result, because
/// neither can see what is already in the field. Started after "I was going to" the result
/// "And then we left" should not have a capital.
/// </para>
/// <para>
/// A wrong lower case is worse than a missed one (a name typed as "london" is a new error;
/// a stray capital is what happened before), so only a first word from
/// <see cref="Lowerable"/> is ever changed. Names ("Will", "May"), "I" and anything with a
/// second capital are never on it.
/// </para>
/// </remarks>
public static class CaretCase
{
    /// <summary>How much text before the caret is worth reading.</summary>
    public const int ContextLength = 12;

    /// <summary>First words that are lower case in the middle of a sentence and never a name.</summary>
    private static readonly HashSet<string> Lowerable = new(StringComparer.Ordinal)
    {
        "a", "about", "after", "all", "also", "although", "always", "am", "an", "and", "any", "are", "as", "at",
        "back", "because", "been", "before", "being", "but", "by", "can", "could", "did", "do", "does", "don't",
        "down", "each", "either", "else", "even", "every", "for", "from", "get", "give", "go", "going", "got",
        "had", "has", "have", "he", "her", "here", "his", "how", "however", "if", "in", "into", "is", "isn't", "it",
        "it's", "its", "just", "know", "less", "let", "like", "make", "many", "maybe", "me", "might", "more",
        "most", "much", "must", "my", "need", "never", "no", "not", "now", "of", "off", "on", "once", "one", "only",
        "or", "other", "our", "out", "over", "perhaps", "please", "plus", "really", "she", "should", "since", "so",
        "some", "still", "such", "than", "that", "that's", "the", "their", "them", "then", "there", "there's",
        "these", "they", "they're", "this", "those", "though", "through", "to", "too", "under", "until", "up",
        "us", "very", "was", "wasn't", "we", "we're", "we've", "were", "what", "when", "where", "whether",
        "which", "while", "who", "why", "with", "without", "won't", "would", "yet", "you", "you're",
        "you've", "your",
    };

    /// <summary>Closing marks that can follow the full stop that ended the last sentence.</summary>
    private const string Closers = "\"')]}”’";

    /// <summary>
    /// <paramref name="text"/> with its first letter lowered when the caret is mid-sentence,
    /// otherwise as it came.
    /// </summary>
    /// <param name="text">What is about to be typed.</param>
    /// <param name="beforeCaret">The text before the caret, or null when unknown.</param>
    public static string Apply(string text, string? beforeCaret)
    {
        if (string.IsNullOrEmpty(text) || !char.IsUpper(text[0]) || !IsMidSentence(beforeCaret)) return text;

        var end = 0;
        while (end < text.Length && (char.IsLetter(text[end]) || text[end] == '\'' || text[end] == '’')) end++;
        var word = text[..end].Replace('’', '\'');
        if (word.Length == 0 || !Lowerable.Contains(word.ToLowerInvariant())) return text;
        if (word.Length > 1 && word.Skip(1).Any(char.IsUpper)) return text;

        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    /// <summary>
    /// True when the last thing before the caret is part of a sentence that has not ended:
    /// a letter, digit, comma, semicolon or dash. Unknown, empty, a new line, a full stop,
    /// question mark, exclamation mark or colon all mean a capital is right.
    /// </summary>
    public static bool IsMidSentence(string? beforeCaret)
    {
        if (string.IsNullOrEmpty(beforeCaret)) return false;

        var i = beforeCaret.Length - 1;
        while (i >= 0 && (beforeCaret[i] == ' ' || beforeCaret[i] == '\t' || beforeCaret[i] == ' ')) i--;
        if (i < 0) return false;

        while (i >= 0 && Closers.Contains(beforeCaret[i])) i--;
        if (i < 0) return false;

        var last = beforeCaret[i];
        if (last is '.' or '!' or '?' or ':' or '\n' or '\r' or '…') return false;
        return char.IsLetterOrDigit(last) || last is ',' or ';' or '-' or '–' or '—';
    }
}
