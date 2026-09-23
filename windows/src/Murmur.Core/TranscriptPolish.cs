namespace Murmur.Core;

/// <summary>
/// Small, deterministic tidying of a transcript before it is typed.
/// </summary>
/// <remarks>
/// <para>
/// Parakeet was trained on complete sentences, so it closes every utterance with a full
/// stop — including "can you send me the Q2 numbers", which the user is about to follow with
/// more typing in a chat box. This is the rules layer that knows the difference. It is not
/// a language model: it never rewords, never removes a word, and every rule here is
/// something a person can predict.
/// </para>
/// <para>Shared contract in spirit with the macOS <c>RuleBasedFormatter</c>.</para>
/// </remarks>
public static class TranscriptPolish
{
    /// <summary>
    /// Drops a trailing full stop when the transcript is a single sentence.
    /// </summary>
    /// <remarks>
    /// A single sentence is a fragment or a chat message; a full stop there is noise the
    /// user has to delete. Two or more sentences read as prose and keep their punctuation.
    /// Question marks and exclamation marks always stay — they carry meaning.
    /// </remarks>
    public static string DropTrailingFullStopIfSingleSentence(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0 || trimmed[^1] != '.') return text;

        // "..." is deliberate; leave it.
        if (trimmed.Length >= 2 && trimmed[^2] == '.') return text;

        // Any earlier sentence terminator means this is prose.
        var body = trimmed[..^1];
        if (body.Any(static c => c is '.' or '?' or '!')) return text;

        return body;
    }

    /// <summary>Applies the enabled rules.</summary>
    public static string Apply(string text, bool dropSingleSentenceFullStop) =>
        Apply(text, dropSingleSentenceFullStop ? TrailingFullStop.DropAfterSingleSentence : TrailingFullStop.Keep);

    /// <summary>Applies the chosen full-stop rule.</summary>
    public static string Apply(string text, TrailingFullStop rule) => rule switch
    {
        TrailingFullStop.DropAfterSingleSentence => DropTrailingFullStopIfSingleSentence(text),
        TrailingFullStop.Never => DropTrailingFullStop(text),
        _ => text,
    };

    /// <summary>Drops a single trailing full stop whatever came before it. Ellipses stay.</summary>
    public static string DropTrailingFullStop(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0 || trimmed[^1] != '.') return text;
        if (trimmed.Length >= 2 && trimmed[^2] == '.') return text;
        return trimmed[..^1];
    }

    /// <summary>
    /// Removes the comma before "and": "A, B, and C" is "A, B and C", and "we did X, and
    /// then Y" is "we did X and then Y".
    /// </summary>
    /// <remarks>
    /// A house-style rule, enforced here because the generative tier does not keep it:
    /// with "Don't put commas before and" in the user's own instructions, Gemini still
    /// introduced 45 of them in a week. It is a switch, since now and then the comma
    /// closes an aside and a user who does not hold the rule should not lose it.
    /// </remarks>
    public static string RemoveCommaBeforeAnd(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @",(\s+)(and)\b", "$1$2", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

/// <summary>
/// The handful of American spellings the speech model produces, written the British way.
/// </summary>
/// <remarks>
/// Deliberately conservative: a fixed set of families, whole words only, never inside a
/// dotted, hyphenated or camel-cased token, so a code identifier dictated as prose is left
/// alone. "Program" and "licence" are ambiguous in en-GB and are not touched.
/// </remarks>
public static partial class BritishSpellings
{
    /// <summary>Words that end in -ize and are spelt that way in British English too.</summary>
    private static readonly HashSet<string> IzeExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "size", "seize", "prize", "capsize", "resize", "downsize", "upsize", "oversize", "undersize", "maize", "baize", "assize",
    };

    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["behavior"] = "behaviour", ["behaviors"] = "behaviours", ["behavioral"] = "behavioural",
        ["favor"] = "favour", ["favors"] = "favours", ["favored"] = "favoured", ["favorite"] = "favourite", ["favorites"] = "favourites", ["favorable"] = "favourable",
        ["color"] = "colour", ["colors"] = "colours", ["colored"] = "coloured", ["colorful"] = "colourful",
        ["center"] = "centre", ["centers"] = "centres", ["centered"] = "centred",
        ["gray"] = "grey", ["catalog"] = "catalogue", ["catalogs"] = "catalogues",
        ["modeling"] = "modelling", ["modeled"] = "modelled", ["traveling"] = "travelling", ["traveled"] = "travelled", ["canceled"] = "cancelled", ["canceling"] = "cancelling",
        ["fulfill"] = "fulfil", ["fulfillment"] = "fulfilment", ["enroll"] = "enrol", ["enrollment"] = "enrolment",
        ["analyze"] = "analyse", ["analyzed"] = "analysed", ["analyzing"] = "analysing", ["paralyze"] = "paralyse",
        ["defense"] = "defence", ["offense"] = "offence", ["license"] = "licence", ["practise"] = "practise",
    };

    /// <summary>Applies the map to whole words.</summary>
    public static string Apply(string text) => Word().Replace(text, m => Replace(m.Value));

    private static string Replace(string word)
    {
        if (Words.TryGetValue(word, out var british)) return MatchCase(word, british);

        var lower = word.ToLowerInvariant();
        string? replaced = null;
        if (lower.EndsWith("ization", StringComparison.Ordinal) || lower.EndsWith("izations", StringComparison.Ordinal))
        {
            replaced = word[..word.IndexOf("iz", StringComparison.OrdinalIgnoreCase)] + Case(word, "is") + word[(word.IndexOf("iz", StringComparison.OrdinalIgnoreCase) + 2)..];
        }
        else if ((lower.EndsWith("ize", StringComparison.Ordinal) || lower.EndsWith("izes", StringComparison.Ordinal) || lower.EndsWith("ized", StringComparison.Ordinal) || lower.EndsWith("izing", StringComparison.Ordinal))
                 && lower.Length >= 6 && !IzeExceptions.Contains(Stem(lower)))
        {
            var at = lower.LastIndexOf("iz", StringComparison.Ordinal);
            replaced = word[..at] + Case(word[at..(at + 2)], "is") + word[(at + 2)..];
        }

        return replaced ?? word;
    }

    /// <summary>"organizing" and "organized" back to "organize", so the exception list is checked on one form.</summary>
    private static string Stem(string lower)
    {
        if (lower.EndsWith("izing", StringComparison.Ordinal)) return lower[..^3] + "e";
        if (lower.EndsWith("ized", StringComparison.Ordinal) || lower.EndsWith("izes", StringComparison.Ordinal)) return lower[..^1];
        return lower;
    }

    private static string Case(string original, string replacement) =>
        original.Length > 0 && original.All(char.IsUpper) ? replacement.ToUpperInvariant()
        : original.Length > 0 && char.IsUpper(original[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
        : replacement;

    private static string MatchCase(string original, string replacement)
    {
        if (original.All(char.IsUpper)) return replacement.ToUpperInvariant();
        return char.IsUpper(original[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
    }

    // A whole word with nothing token-like touching it: no dot, hyphen, underscore or digit
    // either side, and no capital immediately after (camelCase).
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\p{L}\p{N}._\-])\p{L}+(?![\p{L}\p{N}._\-])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex Word();
}

/// <summary>
/// Sanity checks on what a generative clean-up returns, so a model that summarises or
/// invents never reaches the user's text field.
/// </summary>
public static class CleanupGuard
{
    /// <summary>
    /// Every non-empty utterance is worth a round trip when Polished mode is enabled. A
    /// single name or term is exactly where the local recogniser's best guess most often
    /// needs the speaker's vocabulary and the generative model's context.
    /// </summary>
    public const int MinimumWords = 1;

    /// <summary>A result with fewer than this share of the input's words was summarised.</summary>
    public const double MinimumRatio = 0.55;

    /// <summary>A result with more than this share of the input's words had things added.</summary>
    public const double MaximumRatio = 1.6;

    /// <summary>
    /// Words a tidy may drop or add regardless of ratio, so "um so hello there" can
    /// become "Hello there" without tripping the summarising check.
    /// </summary>
    public const int Slack = 2;

    /// <summary>Whether the raw text is long enough to be worth cleaning.</summary>
    public static bool IsWorthCleaning(string raw) => Words(raw) >= MinimumWords;

    /// <summary>
    /// Whether <paramref name="cleaned"/> is a plausible tidy of <paramref name="raw"/>
    /// rather than a rewrite. Null or blank is never accepted.
    /// </summary>
    public static bool IsPlausible(string raw, string? cleaned)
    {
        if (string.IsNullOrWhiteSpace(cleaned)) return false;

        var before = Words(raw);
        var after = Words(cleaned);

        // A short utterance is often a question or an instruction, which a model may answer
        // or obey, and an answer is longer than the question, so additions get only 1 word of
        // slack there; dropping fillers keeps the full slack.
        var floor = Math.Min(before - Slack, (int)Math.Ceiling(before * MinimumRatio));
        var ceiling = before < 6 ? before + 1 : Math.Max(before + Slack, (int)Math.Floor(before * MaximumRatio));
        return after >= Math.Max(1, floor) && after <= ceiling;
    }

    private static int Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
