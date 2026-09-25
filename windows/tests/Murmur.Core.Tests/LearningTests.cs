using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>A read-back that starts part-way through the dictation is not an edit.</summary>
public sealed class ClippedReadTests
{
    // Seen in Claude's window on 25/09/2026, with nothing changed by the user.
    private const string Rest = "Firstly, the ordering of the tabs, which I talked about before, which you haven't sorted out, and also the finding section, making that world-class in terms of a very clear workflow";
    private const string Typed = "How are we getting on with this whole prepayments and accruals? " + Rest;
    private const string Clipped = "ccruals? " + Rest;

    [Fact]
    public void A_start_cut_mid_word_is_put_back()
    {
        EditAlignment.StartsMidWord(Typed, Clipped).ShouldBeTrue();
        EditAlignment.RestoreClippedStart(Typed, Clipped, wholeWords: false).ShouldBe(Typed);
    }

    [Fact]
    public void Whole_missing_words_are_put_back_only_with_other_evidence()
    {
        const string typed = "And the VAT return, is there any way to change it";
        const string found = "the VAT return, is there any way to change it";

        EditAlignment.StartsMidWord(typed, found).ShouldBeFalse();
        EditAlignment.RestoreClippedStart(typed, found, wholeWords: false).ShouldBe(found);
        EditAlignment.RestoreClippedStart(typed, found, wholeWords: true).ShouldBe(typed);
    }

    [Fact]
    public void A_real_rewrite_of_the_start_is_left_alone()
    {
        const string typed = "Sounds good, send the Sarif version";
        const string found = "Great, thanks. Send the serif version";

        EditAlignment.RestoreClippedStart(typed, found, wholeWords: true).ShouldBe(found);
    }

    [Fact]
    public void Letters_lost_off_the_end_of_a_word_are_not_a_fix()
    {
        DictionarySuggestions.IsWorthOffering("And the Circle", "ircle").ShouldBeFalse();
        DictionarySuggestions.IsWorthOffering("accruals", "ccruals").ShouldBeFalse();
    }

    private static EditWatcher Fast(ITextInjector injector) => new(injector)
    {
        FirstLook = TimeSpan.FromMilliseconds(10),
        Interval = TimeSpan.FromMilliseconds(20),
        Duration = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public async Task A_read_that_starts_inside_the_dictation_is_widened()
    {
        var conversation = "Earlier in the chat. ";
        var injector = new RecordingTextInjector
        {
            FocusTarget = "claude",
            // Only a read reaching well back finds the whole dictation.
            AroundCaretFor = before => before > Typed.Length + EditWatcher.Margin ? conversation + Typed : Clipped,
        };
        var watcher = Fast(injector);
        DictationEdit? edit = null;
        watcher.Finished += (_, e) => edit = e;

        watcher.Watch(DateTimeOffset.UnixEpoch, Typed, "claude", textBefore: true);
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= 4)).ShouldBeTrue();
        watcher.Stop();
        await watcher.Current.WaitAsync(Wait.Timeout);

        edit.ShouldNotBeNull().IsEdited.ShouldBeFalse();
        edit.Final.ShouldBe(Typed);
    }

    [Fact]
    public async Task A_clipped_read_missing_too_much_to_match_is_widened_rather_than_given_up_on()
    {
        var injector = new RecordingTextInjector
        {
            FocusTarget = "claude",
            AroundCaretFor = before => before > Typed.Length + EditWatcher.Margin ? "Earlier. " + Typed : "the tabs",
        };
        var watcher = Fast(injector);
        DictationEdit? edit = null;
        watcher.Finished += (_, e) => edit = e;

        watcher.Watch(DateTimeOffset.UnixEpoch, Typed, "claude", textBefore: true);
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= 4)).ShouldBeTrue();
        watcher.Stop();
        await watcher.Current.WaitAsync(Wait.Timeout);

        edit.ShouldNotBeNull().Final.ShouldBe(Typed);
    }

    [Fact]
    public async Task A_read_that_never_reaches_the_start_takes_it_as_typed_and_still_sees_a_fix_later_on()
    {
        var injector = new RecordingTextInjector { FocusTarget = "claude", AroundCaretText = Clipped.Replace("tabs", "tables", StringComparison.Ordinal) };
        var watcher = Fast(injector);
        DictationEdit? edit = null;
        watcher.Finished += (_, e) => edit = e;

        watcher.Watch(DateTimeOffset.UnixEpoch, Typed, "claude", textBefore: true);
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= 2)).ShouldBeTrue();
        watcher.Stop();
        await watcher.Current.WaitAsync(Wait.Timeout);

        edit.ShouldNotBeNull().Changes.ShouldBe([new WordChange("tabs", "tables")]);
        edit.Final.ShouldStartWith("How are we getting on");
    }
}

/// <summary>Fixes the user makes become dictionary entries, judged first.</summary>
public sealed class EditLearnerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"acapella-learn-{Guid.NewGuid():N}");
    private readonly DictionaryFile _dictionary;
    private readonly SuggestionStore _suggestions;

    public EditLearnerTests()
    {
        Directory.CreateDirectory(_folder);
        _dictionary = new DictionaryFile(Path.Combine(_folder, "dictionary.txt"));
        _suggestions = new SuggestionStore(Path.Combine(_folder, "suggestions.json"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static DictationEdit Edit(string typed, string final, int minute = 0) =>
        new(new DateTimeOffset(2026, 9, 25, 12, minute, 0, TimeSpan.FromHours(1)), typed, final, EditAlignment.Changes(typed, final), EditAlignment.WordErrorRate(typed, final));

    private static FakeDecisionModel Jev(double mishearing)
    {
        var jev = new FakeDecisionModel();
        jev.Answers["mishearing"] = new Decision("mishearing", mishearing, null, null);
        jev.Release.SetResult();
        return jev;
    }

    private bool Has(string hear, string write) =>
        _dictionary.Entries.Any(e => e.Kind == EntryKind.Correction && e.Hear == hear && e.Write == write);

    [Fact]
    public async Task A_fix_the_model_is_sure_was_a_mishearing_is_learnt_at_once()
    {
        var jev = Jev(0.93);
        var learner = new EditLearner(_dictionary, _suggestions, () => jev, () => true);

        await learner.LearnAsync(Edit("run it over the Sarif headings", "run it over the serif headings"));

        Has("Sarif", "serif").ShouldBeTrue();
        _suggestions.Pending.ShouldBeEmpty();
        var learnt = _suggestions.Learnt.ShouldHaveSingleItem();
        learnt.LearntBecause.ShouldNotBeNull().ShouldContain("Jev");
        jev.Calls.ShouldHaveSingleItem().State.ShouldContain("\"Sarif\" with \"serif\"");
    }

    [Fact]
    public async Task A_change_of_mind_is_never_offered()
    {
        var learner = new EditLearner(_dictionary, _suggestions, () => Jev(0.05), () => true);

        await learner.LearnAsync(Edit("meet at the Brixton office", "meet at the Brighton office"));

        _dictionary.Entries.ShouldBeEmpty();
        _suggestions.Pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_model_the_same_fix_twice_is_learnt()
    {
        var learner = new EditLearner(_dictionary, _suggestions, () => null, () => true);

        await learner.LearnAsync(Edit("ask Gev about it", "ask Jev about it"));
        Has("Gev", "Jev").ShouldBeFalse();
        _suggestions.Pending.ShouldHaveSingleItem().Count.ShouldBe(1);

        await learner.LearnAsync(Edit("Gev said no", "Jev said no", minute: 5));
        Has("Gev", "Jev").ShouldBeTrue();
        _suggestions.Learnt.ShouldHaveSingleItem().LearntBecause.ShouldBe("you made this fix 2 times");
    }

    [Fact]
    public async Task Switched_off_a_sure_fix_only_waits_as_a_suggestion()
    {
        var learner = new EditLearner(_dictionary, _suggestions, () => Jev(0.95), () => false);

        await learner.LearnAsync(Edit("the Sarif font", "the serif font"));

        _dictionary.Entries.ShouldBeEmpty();
        _suggestions.Pending.ShouldHaveSingleItem().Hear.ShouldBe("Sarif");
    }

    [Fact]
    public async Task Undo_takes_it_out_of_the_dictionary_and_it_is_never_learnt_again()
    {
        var learner = new EditLearner(_dictionary, _suggestions, () => Jev(0.95), () => true);
        await learner.LearnAsync(Edit("the Sarif font", "the serif font"));

        EditLearner.Undo(_suggestions.Learnt.Single(), _dictionary, _suggestions);
        await learner.LearnAsync(Edit("a Sarif heading", "a serif heading", minute: 9));

        _dictionary.Entries.ShouldBeEmpty();
        _suggestions.Learnt.ShouldBeEmpty();
        _suggestions.Pending.ShouldBeEmpty();
    }
}

/// <summary>Which kind of writing an app is for.</summary>
public sealed class AppStyleTests
{
    [Theory]
    [InlineData("claude", "Claude", WritingStyle.Prompt)]
    [InlineData("slack", "Slack | general | Sidgrove", WritingStyle.Chat)]
    [InlineData("OUTLOOK", "Inbox - dave@sidgrove.com - Outlook", WritingStyle.Email)]
    [InlineData("chrome", "Inbox (3) - dave@sidgrove.com - Gmail - Google Chrome", WritingStyle.Email)]
    [InlineData("chrome", "ChatGPT - Google Chrome", WritingStyle.Prompt)]
    [InlineData("msedge", "Q3 board pack - Google Docs - Microsoft Edge", WritingStyle.Document)]
    [InlineData("chrome", "Acme internal tool - Google Chrome", WritingStyle.Unknown)]
    [InlineData("chrome", "WordPress admin - Google Chrome", WritingStyle.Unknown)]
    [InlineData("EXCEL", "Book1 - Excel", WritingStyle.Unknown)]
    public void The_rules_place_the_apps_Dave_uses(string app, string title, WritingStyle expected)
    {
        AppStyles.FromWindow(new FocusedWindow(app, title)).ShouldBe(expected);
    }

    [Fact]
    public void An_unsure_or_other_answer_changes_nothing()
    {
        AppStyles.FromDecision(new Decision("style", null, "email", 0.9)).ShouldBe(WritingStyle.Email);
        AppStyles.FromDecision(new Decision("style", null, "email", 0.4)).ShouldBe(WritingStyle.Unknown);
        AppStyles.FromDecision(new Decision("style", null, "other", 0.99)).ShouldBe(WritingStyle.Unknown);
        AppStyles.FromDecision(null).ShouldBe(WritingStyle.Unknown);
    }
}

/// <summary>The history as training examples.</summary>
public sealed class TrainingSetTests
{
    [Fact]
    public void Only_dictations_read_back_have_a_final_text_and_edited_ones_carry_the_users_words()
    {
        var at = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(1));
        TranscriptRecord[] records =
        [
            new() { At = at, RawText = "the sarif font", Text = "The Sarif font", EditChecked = true, EditedText = "The serif font", App = "slack", Style = "Chat" },
            new() { At = at.AddMinutes(1), RawText = "left alone", Text = "Left alone", EditChecked = true },
            new() { At = at.AddMinutes(2), RawText = "never read", Text = "Never read" },
            new() { At = at.AddMinutes(3), Text = "no raw text, from before it was kept" },
        ];

        var examples = TrainingSet.From(records, a => a == at ? "corrected.wav" : null);

        examples.Count.ShouldBe(3);
        examples[0].Final.ShouldBe("The serif font");
        examples[0].Audio.ShouldBe("corrected.wav");
        examples[0].Edited.ShouldBeTrue();
        examples[1].Final.ShouldBe("Left alone");
        examples[2].Final.ShouldBeNull();
        TrainingSet.ToJsonLines(examples).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(3);
    }
}
