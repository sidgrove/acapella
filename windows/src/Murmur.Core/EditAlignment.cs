namespace Murmur.Core;

/// <summary>A run of typed words the user replaced with other words.</summary>
/// <param name="Typed">The words as typed.</param>
/// <param name="Final">The words the user left in their place.</param>
public sealed record WordChange(string Typed, string Final);

/// <summary>
/// Finds a typed dictation in the text of the field it went into, and says how the user
/// changed it.
/// </summary>
/// <remarks>
/// <para>
/// Word by word, not character by character: what matters is which words were swapped, and
/// a field read back through UI Automation differs from what was typed in ways that are not
/// edits: smart quotes, a trailing space, the text on either side.
/// </para>
/// <para>
/// Words compare without case or the punctuation around them, so a capital the user took off
/// after deleting the first word, or a comma added, is not a changed word. A case change
/// that makes a name (github to GitHub) is kept as one.
/// </para>
/// </remarks>
public static class EditAlignment
{
    /// <summary>
    /// The field's version of <paramref name="typed"/>: the stretch of <paramref name="field"/>
    /// that best matches it, with the user's changes. Null when it is not there, because it
    /// was sent, cleared or never landed.
    /// </summary>
    /// <param name="typed">What was typed.</param>
    /// <param name="field">The text read back from the field, with whatever surrounds it.</param>
    /// <param name="maxDifference">The largest share of the typed words that may differ before it counts as gone.</param>
    public static string? Find(string typed, string field, double maxDifference = 0.5)
    {
        var t = Words(typed);
        var f = Words(field);
        if (t.Count == 0 || f.Count == 0) return null;

        // Semi-global: the typed words must all be accounted for, the field's words before
        // and after them are free.
        var n = t.Count;
        var m = f.Count;
        var cost = new int[n + 1, m + 1];
        for (var i = 1; i <= n; i++) cost[i, 0] = i;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var substitute = cost[i - 1, j - 1] + (t[i - 1].Key == f[j - 1].Key ? 0 : 1);
                cost[i, j] = Math.Min(substitute, Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1));
            }
        }

        // On a tie the later match wins: the newest dictation is the one nearest the caret.
        var end = 1;
        for (var j = 2; j <= m; j++) if (cost[n, j] <= cost[n, end]) end = j;
        if (cost[n, end] > maxDifference * n) return null;

        // Walk back to where the match starts in the field.
        int row = n, column = end;
        while (row > 0 && column > 0)
        {
            var here = cost[row, column];
            if (here == cost[row - 1, column - 1] + (t[row - 1].Key == f[column - 1].Key ? 0 : 1)) { row--; column--; }
            else if (here == cost[row - 1, column] + 1) row--;
            else column--;
        }
        var start = column;   // index of the first field word in the match
        if (start >= end) return null;

        return field[f[start].Start..(f[end - 1].Start + f[end - 1].Text.Length)];
    }

    /// <summary>
    /// Whether <paramref name="found"/> sits at the very start of <paramref name="field"/>, so
    /// the read may have begun part-way through the dictation rather than before it.
    /// </summary>
    public static bool TouchesStart(string field, string found)
    {
        var at = field.LastIndexOf(found, StringComparison.Ordinal);
        return at >= 0 && string.IsNullOrWhiteSpace(field[..at]);
    }

    /// <summary>
    /// Whether the first word of <paramref name="found"/> is only the tail of a typed word
    /// ("ccruals" for "accruals"): a read that began mid-word, never an edit.
    /// </summary>
    public static bool StartsMidWord(string typed, string found) => ClippedStart(typed, found, wholeWords: false) is not null;

    /// <summary>
    /// <paramref name="found"/> with the typed words before the first word it shows put back,
    /// for a read that began part-way through the dictation (seen in Claude's window on
    /// 25/09/2026: "ccruals? Firstly" for "…prepayments and accruals? Firstly"). Unchanged
    /// when nothing is missing or the missing part looks like an edit.
    /// </summary>
    /// <param name="typed">What was typed.</param>
    /// <param name="found">The field's version of it, from <see cref="Find"/>.</param>
    /// <param name="wholeWords">
    /// Also restore whole words missing from the start. Only when there is other evidence the
    /// read was cut short, because a user deleting a leading "And" looks exactly the same.
    /// </param>
    public static string RestoreClippedStart(string typed, string found, bool wholeWords) => ClippedStart(typed, found, wholeWords) ?? found;

    private static string? ClippedStart(string typed, string found, bool wholeWords)
    {
        var t = Words(typed);
        var f = Words(found);
        var (ops, _) = Align(t, f);

        // Walk to the first word the two share.
        int i = 0, j = 0, paired = -1;
        foreach (var op in ops)
        {
            if (op == Op.Same) break;
            if (op == Op.Substitute) paired = i;
            if (op is Op.Substitute or Op.Delete) i++;
            if (op is Op.Substitute or Op.Insert) j++;
        }
        if (i == 0 || i >= t.Count || j >= f.Count) return null;

        // More than a fragment of one word ahead of the match is the user's own writing.
        if (j > 1) return null;
        if (j == 1)
        {
            if (paired < 0) return null;
            var fragment = f[0].Bare;
            var word = t[paired].Bare;
            if (fragment.Length >= word.Length || !word.EndsWith(fragment, StringComparison.OrdinalIgnoreCase)) return null;
        }
        else if (!wholeWords)
        {
            return null;
        }

        return typed[..t[i].Start] + found[f[j].Start..];
    }

    /// <summary>The runs of words the user replaced, in order. Words only added or only removed are not listed.</summary>
    public static IReadOnlyList<WordChange> Changes(string typed, string final)
    {
        var t = Words(typed);
        var f = Words(final);
        var changes = new List<WordChange>();

        var (ops, _) = Align(t, f);
        var i = 0;
        var j = 0;
        var typedRun = new List<string>();
        var finalRun = new List<string>();

        void Flush()
        {
            if (typedRun.Count > 0 && finalRun.Count > 0) changes.Add(new WordChange(string.Join(' ', typedRun), string.Join(' ', finalRun)));
            typedRun.Clear();
            finalRun.Clear();
        }

        foreach (var op in ops)
        {
            switch (op)
            {
                case Op.Same:
                    if (IsNameCaseChange(t[i].Bare, f[j].Bare))
                    {
                        typedRun.Add(t[i].Bare);
                        finalRun.Add(f[j].Bare);
                    }
                    else
                    {
                        Flush();
                    }
                    i++;
                    j++;
                    break;
                case Op.Substitute:
                    typedRun.Add(t[i++].Bare);
                    finalRun.Add(f[j++].Bare);
                    break;
                case Op.Delete:
                    typedRun.Add(t[i++].Bare);
                    break;
                case Op.Insert:
                    finalRun.Add(f[j++].Bare);
                    break;
            }
        }
        Flush();
        return changes;
    }

    /// <summary>Word error rate of <paramref name="typed"/> against <paramref name="final"/>: changed words over the words that should have been typed.</summary>
    public static double WordErrorRate(string typed, string final)
    {
        var f = Words(final);
        var (_, distance) = Align(Words(typed), f);
        return f.Count == 0 ? (distance == 0 ? 0 : 1) : (double)distance / f.Count;
    }

    private enum Op { Same, Substitute, Delete, Insert }

    private static (List<Op> Ops, int Distance) Align(List<Word> t, List<Word> f)
    {
        var n = t.Count;
        var m = f.Count;
        var cost = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) cost[i, 0] = i;
        for (var j = 0; j <= m; j++) cost[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var substitute = cost[i - 1, j - 1] + (t[i - 1].Key == f[j - 1].Key ? 0 : 1);
                cost[i, j] = Math.Min(substitute, Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1));
            }
        }

        var ops = new List<Op>(n + m);
        int row = n, column = m;
        while (row > 0 || column > 0)
        {
            if (row > 0 && column > 0 && cost[row, column] == cost[row - 1, column - 1] + (t[row - 1].Key == f[column - 1].Key ? 0 : 1))
            {
                ops.Add(t[row - 1].Key == f[column - 1].Key ? Op.Same : Op.Substitute);
                row--;
                column--;
            }
            else if (row > 0 && cost[row, column] == cost[row - 1, column] + 1)
            {
                ops.Add(Op.Delete);
                row--;
            }
            else
            {
                ops.Add(Op.Insert);
                column--;
            }
        }
        ops.Reverse();
        return (ops, cost[n, m]);
    }

    /// <summary>
    /// A case change that makes a name, as opposed to the capital a sentence start gains or
    /// loses: the new form has a capital after its first letter (GitHub, PAYE, iPhone).
    /// </summary>
    private static bool IsNameCaseChange(string typed, string final) =>
        typed != final && final.Skip(1).Any(char.IsUpper) && !typed.Skip(1).Any(char.IsUpper);

    private readonly record struct Word(string Text, int Start, string Bare, string Key);

    private static List<Word> Words(string text)
    {
        var words = new List<Word>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            if (i == start) continue;

            var token = text[start..i];
            var bare = token.Trim(Punctuation);
            if (bare.Length == 0) continue;
            words.Add(new Word(token, start, bare, bare.Replace('’', '\'').ToLowerInvariant()));
        }
        return words;
    }

    private static readonly char[] Punctuation = ['.', ',', ';', ':', '!', '?', '"', '\'', '“', '”', '‘', '’', '(', ')', '[', ']', '*', '_', '…'];
}
