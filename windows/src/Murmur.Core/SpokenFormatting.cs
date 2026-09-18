using System.Text;
using System.Text.RegularExpressions;

namespace Murmur.Core;

/// <summary>
/// Deterministic spoken commands and filler removal, applied before any generative tier.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes local-only mode feel finished: "new line", "full stop" and "scratch
/// that" work without a network, and "um" never reaches the page. It also shrinks the job
/// the AI clean-up has left to do, which makes that tier more predictable.
/// </para>
/// <para>
/// Every rule is a plain pattern a person can predict. Parakeet already punctuates and
/// capitalises, so each command tolerates a comma or full stop on either side of it and the
/// result is re-capitalised after the breaks it introduces.
/// </para>
/// </remarks>
public static partial class SpokenFormatting
{
    /// <summary>Words dropped when they stand alone as hesitation.</summary>
    public static readonly IReadOnlyList<string> DefaultFillers = ["um", "umm", "uh", "uhh", "er", "erm", "hmm", "mm"];

    /// <summary>Applies everything.</summary>
    public static string Apply(string text, bool commands = true, bool fillers = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var result = text;
        if (fillers) result = RemoveFillers(result);
        if (commands)
        {
            result = ApplyScratchThat(result);
            result = ApplyHyphen(result);
            result = ApplyPunctuationCommands(result);
            result = ApplyBreaks(result);
        }

        // Untouched text goes back exactly as it came: the speech model already capitalises,
        // and this layer only mends what its own edits disturbed.
        return result == text ? text : Tidy(result);
    }

    /// <summary>Removes standalone hesitation words and the punctuation that clung to them.</summary>
    public static string RemoveFillers(string text) => Filler().Replace(text, " ");

    /// <summary>
    /// "scratch that" and friends remove the clause spoken just before them, back to the
    /// previous sentence end or the start of the text.
    /// </summary>
    public static string ApplyScratchThat(string text)
    {
        var result = text;
        Match match;
        while ((match = Scratch().Match(result)).Success)
        {
            var before = result[..match.Index];
            var cut = LastClauseStart(before);
            result = string.Concat(before.AsSpan(0, cut), " ", result.AsSpan(match.Index + match.Length));
        }

        return result;
    }

    /// <summary>"full stop", "comma", "question mark" and so on become the mark itself.</summary>
    /// <remarks>
    /// "Period" and "dash" are deliberately not commands. This is an en-GB product and a
    /// week of real history had "period" sixteen times, every one the noun (a VAT period, a
    /// pay period), thirteen of them mangled into a full stop; "dash" fired twice, both
    /// wrong, and produced the en dash the user's own rules forbid.
    /// </remarks>
    public static string ApplyPunctuationCommands(string text)
    {
        var result = PunctuationWord().Replace(text, m => MarkFor(m.Groups["word"].Value));
        return Spacing().Replace(result, "$1");
    }

    /// <summary>A spoken "hyphen" between two words joins them: "twenty hyphen five" is "twenty-five".</summary>
    public static string ApplyHyphen(string text) => Hyphen().Replace(text, "-");

    /// <summary>"new line" and "new paragraph" become breaks.</summary>
    public static string ApplyBreaks(string text)
    {
        var result = Paragraph().Replace(text, "\n\n");
        return Line().Replace(result, "\n");
    }

    private static int LastClauseStart(string before)
    {
        var trimmed = before.TrimEnd();
        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            if (trimmed[i] is '.' or '?' or '!' or '\n') return i + 1;
        }

        return 0;
    }

    private static string MarkFor(string word) => word.ToLowerInvariant() switch
    {
        "full stop" => ".",
        "comma" => ",",
        "question mark" => "?",
        "exclamation mark" or "exclamation point" => "!",
        "colon" => ":",
        "semicolon" or "semi colon" => ";",
        "open bracket" or "open parenthesis" => " (",
        "close bracket" or "close parenthesis" => ")",
        "open quote" or "open quotes" => " “",
        "close quote" or "close quotes" => "”",
        "ellipsis" or "dot dot dot" => "…",
        _ => word,
    };

    /// <summary>Collapses the spacing the substitutions leave and re-capitalises after breaks and sentence ends.</summary>
    private static string Tidy(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = MultiSpace().Replace(lines[i], " ").Trim();
            line = SpaceBeforeMark().Replace(line, "$1");
            line = MixedMarks().Replace(line, "$1");
            line = DoubleMark().Replace(line, "$1");
            line = SpaceAfterOpener().Replace(line, "$1");
            lines[i] = Capitalise(line);
        }

        var joined = string.Join('\n', lines);
        return TripleBreak().Replace(joined, "\n\n").Trim();
    }

    private static string Capitalise(string line)
    {
        if (line.Length == 0) return line;

        var builder = new StringBuilder(line.Length);
        var atStart = true;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            builder.Append(atStart && char.IsLetter(c) ? char.ToUpperInvariant(c) : c);
            if (char.IsLetterOrDigit(c)) atStart = false;
            // A sentence ends at a mark followed by a space, never inside a token: the dot
            // in "e.g." or "sidgrove.com" used to capitalise the letter after it. An
            // ellipsis trails off rather than ending anything.
            else if (c is '.' or '?' or '!' && (i + 1 == line.Length || char.IsWhiteSpace(line[i + 1])) && !EndsWithAbbreviation(line, i) && !(c == '.' && i > 0 && line[i - 1] == '.'))
            {
                atStart = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Dotted abbreviations whose full stop does not end a sentence.</summary>
    private static readonly string[] Abbreviations = ["e.g", "i.e", "etc", "vs", "Ltd", "Inc", "No", "St", "Mr", "Mrs", "Dr"];

    private static bool EndsWithAbbreviation(string line, int stopIndex)
    {
        if (line[stopIndex] != '.') return false;
        foreach (var abbreviation in Abbreviations)
        {
            var start = stopIndex - abbreviation.Length;
            if (start < 0 || !line.AsSpan(start, abbreviation.Length).Equals(abbreviation, StringComparison.OrdinalIgnoreCase)) continue;
            if (start == 0 || !char.IsLetterOrDigit(line[start - 1])) return true;
        }
        return false;
    }

    // "mm" after a figure is millimetres ("5 mm wide"); "ER" in capitals is the emergency
    // room, which the speech model writes as an acronym. Neither is a hesitation. The
    // lookbehind keeps a filler from being taken out of the middle of "summer".
    [GeneratedRegex(@"(?<=^|[\s,.])(?<!\d\s?)(?:[Uu][Mm]+|[Uu][Hh]+|[Ee][Rr][Mm]|[Ee]r|e[Rr]|[Hh][Mm]{2,}|[Mm][Mm]+)\b[,.]?\s*", RegexOptions.CultureInvariant)]
    private static partial Regex Filler();

    // "Scratch that" is a command only when it ends a clause: "delete that file and push
    // again" is an instruction to someone else, not to this app.
    [GeneratedRegex(@"[,.]?\s*\b(?:scratch that|delete that|strike that)\b(?=\s*(?:[,.;!?]|$))[,.]?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Scratch();

    [GeneratedRegex(@"[,.]?\s*\b(?<word>full stop|comma|question mark|exclamation mark|exclamation point|colon|semicolon|semi colon|open bracket|close bracket|open parenthesis|close parenthesis|open quotes?|close quotes?|ellipsis|dot dot dot)\b[,.]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationWord();

    [GeneratedRegex(@"(?<=\p{L})[,.]?\s+hyphen\s+(?=\p{L}|\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hyphen();

    [GeneratedRegex(@"\s+([.,?!:;)”])", RegexOptions.CultureInvariant)]
    private static partial Regex Spacing();

    // "a new line to the invoice", "another new paragraph" describe a thing; the bare
    // phrase is the command.
    [GeneratedRegex(@",?\s*(?<!\b(?:a|an|the|one|another|each|every|this|that|per)\s+)\b(?:new paragraph|next paragraph)\b[,.]?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Paragraph();

    [GeneratedRegex(@",?\s*(?<!\b(?:a|an|the|one|another|each|every|this|that|per)\s+)\b(?:new line|newline|next line|line break)\b[,.]?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Line();

    // "what is the.?" after a word was removed: the last mark wins.
    [GeneratedRegex(@"[.,;:]+([?!])", RegexOptions.CultureInvariant)]
    private static partial Regex MixedMarks();

    [GeneratedRegex(@"([(“])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceAfterOpener();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"\s+([.,?!:;])", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceBeforeMark();

    // An ellipsis is deliberate and stays; only a mark doubled by an edit ("one, ." after
    // a word came out) is collapsed.
    [GeneratedRegex(@"([.,?!:;])(?:\s+[.,]|,)+", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleMark();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex TripleBreak();
}
