using Murmur.Abstractions;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>
/// Turns the fixes the user makes by hand into dictionary entries, asking the decision model
/// first whether each was a mishearing or a change of mind.
/// </summary>
/// <remarks>
/// <para>
/// A fix goes into the dictionary on its own when the decision model is sure it was a
/// mishearing, or when the user has made the same fix <see cref="AddAfter"/> times. Anything
/// else waits in the Dictionary tab for a yes or a no. A fix the model is sure was the user
/// changing their mind is never offered: repeating it would put words in their mouth.
/// </para>
/// <para>
/// Runs after the watch has ended, minutes after typing, so the model's half-second is
/// never felt. With no model, or no answer, the count alone decides.
/// </para>
/// </remarks>
public sealed class EditLearner
{
    private readonly DictionaryFile _dictionary;
    private readonly SuggestionStore _suggestions;
    private readonly Func<IDecisionModel?> _judge;
    private readonly Func<bool> _addByItself;

    /// <summary>Creates a learner over the user's dictionary and suggestions.</summary>
    /// <param name="dictionary">Where learnt fixes go.</param>
    /// <param name="suggestions">Where fixes wait, and where learnt ones are recorded so they can be undone.</param>
    /// <param name="judge">The decision model to ask, read per edit; null for none.</param>
    /// <param name="addByItself">Whether fixes may go into the dictionary without the user adding them, read per edit.</param>
    public EditLearner(DictionaryFile dictionary, SuggestionStore suggestions, Func<IDecisionModel?> judge, Func<bool> addByItself)
    {
        _dictionary = dictionary;
        _suggestions = suggestions;
        _judge = judge;
        _addByItself = addByItself;
    }

    /// <summary>How many times the same fix is made before it is added without asking.</summary>
    public const int AddAfter = 2;

    /// <summary>The decision model's probability of a mishearing above which one fix is enough.</summary>
    public const double SureMishearing = 0.85;

    /// <summary>The probability below which the fix is taken to be a change of mind and dropped.</summary>
    public const double SureChangeOfMind = 0.2;

    /// <summary>Learns what it can from one finished edit. Never throws.</summary>
    public async Task LearnAsync(DictationEdit edit, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var (hear, write) in DictionarySuggestions.From(edit.Changes, _dictionary.Entries))
            {
                var mishearing = await JudgeAsync(edit, hear, write, cancellationToken).ConfigureAwait(false);
                var verdict = mishearing is { } p ? $"Jev {p:0.00}" : "not judged";
                if (mishearing < SureChangeOfMind)
                {
                    Log.Info($"dictionary suggestion: {hear} -> {write} left out ({verdict}: a change of mind)");
                    continue;
                }

                var suggestion = _suggestions.Offer(hear, write, DictionarySuggestions.Example(edit.Final, write), edit.At);
                if (suggestion.Dismissed) continue;

                var because = mishearing >= SureMishearing ? "Jev was sure it was a mishearing"
                    : suggestion.Count >= AddAfter ? $"you made this fix {suggestion.Count} times"
                    : null;
                if (because is null || !_addByItself())
                {
                    Log.Info($"dictionary suggestion: {hear} -> {write} ({verdict}; fixed {suggestion.Count}x)");
                    continue;
                }

                // Checked again at the last moment: the user may have added it by hand meanwhile.
                if (!_dictionary.Entries.Any(e => e.Kind == EntryKind.Correction && string.Equals(e.Hear.Trim(), hear, StringComparison.OrdinalIgnoreCase)))
                {
                    _dictionary.Add(DictionaryEntry.Correction(hear, write));
                }
                _suggestions.MarkLearnt(suggestion.Id, edit.At, because);
                Log.Info($"dictionary: learnt {hear} -> {write} ({because}; {verdict})");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"could not learn from an edit: {e.Message}");
        }
    }

    /// <summary>Undoes a fix added on its own: out of the dictionary, and never offered again.</summary>
    public static void Undo(DictionarySuggestion learnt, DictionaryFile dictionary, SuggestionStore suggestions)
    {
        foreach (var entry in dictionary.Entries.Where(e => e.Kind == EntryKind.Correction
                     && string.Equals(e.Hear.Trim(), learnt.Hear, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(e.Write.Trim(), learnt.Write, StringComparison.Ordinal)).ToList())
        {
            dictionary.Remove(entry.Id);
        }
        suggestions.Dismiss(learnt.Id);
        Log.Info($"dictionary: undid learnt {learnt.Hear} -> {learnt.Write}");
    }

    /// <summary>The question put to the decision model. Public so the wording can be tested and reused.</summary>
    public static DecisionQuestion Question { get; } = new("mishearing", "noul",
        "The dictation app's speech recogniser misheard the speaker, and the user's edit spells the words they actually said, rather than the user changing their mind about what to say.",
        new Dictionary<string, string?>
        {
            ["true"] = "The same sounds, spelt right: Sarif to serif, Gev to Jev, get hub to GitHub, Cork Tax to Corp Tax.",
            ["false"] = "Different words chosen after seeing the text: want to need, Tuesday to Wednesday, a rephrasing.",
        });

    /// <summary>What the decision model is shown about one fix.</summary>
    public static string State(DictationEdit edit, string hear, string write) =>
        $"Typed by the dictation app: {DictionarySuggestions.Example(edit.Typed, hear, 120) ?? edit.Typed}\n"
        + $"After the user's edit: {DictionarySuggestions.Example(edit.Final, write, 120) ?? edit.Final}\n"
        + $"The edit replaced \"{hear}\" with \"{write}\".";

    private async Task<double?> JudgeAsync(DictationEdit edit, string hear, string write, CancellationToken cancellationToken)
    {
        if (_judge() is not { } model) return null;
        var answers = await model.DecideAsync(State(edit, hear, write), [Question], cancellationToken).ConfigureAwait(false);
        if (answers is null && model.LastError is { } error && error != "no API key") Log.Warn($"jev edit: no answer ({error})");
        return answers?.FirstOrDefault(a => a.Key == Question.Key)?.Noul;
    }
}
