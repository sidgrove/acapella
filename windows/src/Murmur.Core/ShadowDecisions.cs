using System.Text.RegularExpressions;
using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Asks the decision model the questions the rules have just answered, and logs both.
/// </summary>
/// <remarks>
/// <para>
/// Fire and forget, always: nothing here is awaited by the dictation, so a slow or absent
/// model costs the user nothing. The log lines are the product for now. Once a week of
/// them shows where the model beats the rule, that rule can defer to it; until then the
/// rules stand.
/// </para>
/// <para>
/// Questions are only asked when the rule had something to decide: the send question
/// when the dictation ends in a send word, the period question when "period" was said,
/// the join question at every piece join.
/// </para>
/// </remarks>
public static partial class ShadowDecisions
{
    /// <summary>The questions about a whole dictation, asked once its raw text is known.</summary>
    public static void AboutDictation(DictationEngine engine, string raw, bool ruleSaidSend)
    {
        if (engine.Decisions is not { } model) return;

        var questions = new List<DecisionQuestion>();
        var rules = new List<string>();

        if (ruleSaidSend || EndsWithSendWord(raw, engine))
        {
            questions.Add(new DecisionQuestion("send", "noul",
                "The speaker finishes by telling the dictation app to send or submit the message, rather than using the word as part of what they are writing.",
                new Dictionary<string, string?>
                {
                    ["true"] = "A bare send command at the very end: 'send', 'send it', 'sand'.",
                    ["false"] = "The last words belong to the text: 'ready to send', 'didn't send it', 'what should I send'.",
                }));
            rules.Add($"send={(ruleSaidSend ? "command" : "text")}");
        }

        if (Period().IsMatch(raw))
        {
            questions.Add(new DecisionQuestion("period", "noul",
                "The speaker said 'period' meaning a full stop, as spoken punctuation, rather than a span of time or an accounting, tax or pay period.",
                null));
            rules.Add("period=word");
        }

        var words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words is > 0 and < CleanupGuard.MinimumWords)
        {
            questions.Add(new DecisionQuestion("mishearing", "noul",
                "The transcript contains a word that is probably a mishearing or misspelling of what was meant, and would benefit from correction.",
                null));
            rules.Add("short=skipped");
        }

        questions.Add(new DecisionQuestion("style", "choice",
            "What kind of text is this dictation, judging by its content and tone?",
            new Dictionary<string, string?>
            {
                ["chat"] = "A message to a person or an AI assistant in a chat box.",
                ["email"] = "An email or a formal message with a greeting or sign-off.",
                ["instruction"] = "An instruction to a developer or a tool about software: fix, add, change, build.",
                ["prose"] = "A document, article, post or notes written to be read.",
                ["fragment"] = "A word or two: a name, a label, a value.",
            }));

        Fire(model, "dictation", raw, questions, rules);
    }

    /// <summary>The question at a join between two cleaned pieces.</summary>
    public static void AboutJoin(DictationEngine engine, string first, string second, string joined)
    {
        if (engine.Decisions is not { } model || first.Length == 0 || second.Length == 0) return;

        // Only the end of the earlier text matters for the join, and the model reads
        // literally, so the state is kept to the two sides of the seam.
        var tail = first.Length > 240 ? first[^240..] : first;
        var head = second.Length > 240 ? second[..240] : second;
        var state = $"First part (ends here): {tail}\nSecond part (starts here): {head}";
        var seam = joined.Length > first.TrimEnd().Length ? joined[Math.Max(0, first.TrimEnd().Length - 1)..Math.Min(joined.Length, first.TrimEnd().Length + 2)] : string.Empty;
        var rule = seam.Contains('.') ? "new-sentence" : "continues";

        Fire(model, "join", state,
        [
            new DecisionQuestion("continues", "noul",
                "The second part continues the same sentence as the end of the first part, rather than starting a new sentence.",
                new Dictionary<string, string?>
                {
                    ["true"] = "The words run on: the first part stops mid-thought and the second completes it.",
                    ["false"] = "The first part finishes a sentence and the second begins another.",
                }),
        ], [$"join={rule}"]);
    }

    private static bool EndsWithSendWord(string raw, DictationEngine engine)
    {
        var trimmed = raw.TrimEnd(' ', '.', '!', '?', ',');
        foreach (var word in new[] { engine.SendWord, engine.SendOnlyPhrase }.Concat(engine.SendWordAliases.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            if (!string.IsNullOrWhiteSpace(word) && trimmed.EndsWith(word, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void Fire(IDecisionModel model, string about, string state, List<DecisionQuestion> questions, List<string> rules)
    {
        _ = Task.Run(async () =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var answers = await model.DecideAsync(state, questions, CancellationToken.None).ConfigureAwait(false);
                if (answers is null)
                {
                    Log.Warn($"jev {about}: no answer ({model.LastError ?? "no reason given"}) after {clock.ElapsedMilliseconds} ms");
                    return;
                }
                var said = string.Join(" ", answers.Select(a => a.Noul is { } p ? $"{a.Key}={p:0.00}" : $"{a.Key}={a.Choice}{(a.Confidence is { } c ? $"({c:0.00})" : string.Empty)}"));
                Log.Info($"jev {about}: {said} in {clock.ElapsedMilliseconds} ms; rules: {string.Join(" ", rules)}");
            }
            catch (Exception e)
            {
                Log.Warn($"jev {about}: failed ({e.Message})");
            }
        });
    }

    [GeneratedRegex(@"\bperiod\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Period();
}
