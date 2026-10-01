using Murmur.Core;
using Murmur.Dictionary;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The user's own spellings survive the clean-up (01/10/2026: "yeah -> yeh" never reached the screen).</summary>
public sealed class KeptCorrectionsTests
{
    private static readonly IReadOnlyList<DictionaryEntry> Entries = DictionaryFile.Parse("yeah -> yeh\nReflect -> Reflekt\nSarif -> serif");

    [Fact]
    public void A_lower_case_correction_the_clean_up_undid_is_put_back_keeping_a_sentence_capital()
    {
        var applied = new[] { new AppliedCorrection("yeah", "yeh", 2) };

        KeptCorrections.Apply("Yeah, it's fine. I mean yeah, crack on", applied, Entries)
            .ShouldBe("Yeh, it's fine. I mean yeh, crack on");
    }

    [Fact]
    public void A_name_is_left_to_the_clean_up_and_nothing_is_touched_unless_its_correction_fired()
    {
        KeptCorrections.Apply("Let's reflect on it", [new AppliedCorrection("Reflect", "Reflekt", 1)], Entries).ShouldBe("Let's reflect on it");
        KeptCorrections.Apply("Yeah, fine", [], Entries).ShouldBe("Yeah, fine");
        KeptCorrections.Apply("Yeah, the Sarif one", [new AppliedCorrection("Sarif", "serif", 1)], Entries).ShouldBe("Yeah, the serif one");
    }
}

/// <summary>Reads that pick up the app around the field are not the user's edits.</summary>
public sealed class ReadBackArtefactTests
{
    [Theory]
    [InlineData("How do we make that better?", "GHow do we make that better?", "How do we make that better?")]
    [InlineData("the ChatGPT logo is tiny", "Pensionthe ChatGPT logo is tiny", "the ChatGPT logo is tiny")]
    [InlineData("I said sound the alarm", "on?I said sound the alarm", "I said sound the alarm")]
    [InlineData("a plan", "banana plan", "banana plan")]
    [InlineData("Sarif headings", "serif headings", "serif headings")]
    public void Text_glued_onto_the_first_word_is_taken_off(string typed, string found, string expected)
    {
        EditAlignment.TrimGluedStart(typed, found).ShouldBe(expected);
    }
}

/// <summary>Fixes are learnt the first time when the words are ones the user hardly says.</summary>
public sealed class FirstFixTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"acapella-first-{Guid.NewGuid():N}");
    private readonly DictionaryFile _dictionary;
    private readonly SuggestionStore _suggestions;

    public FirstFixTests()
    {
        Directory.CreateDirectory(_folder);
        _dictionary = new DictionaryFile(Path.Combine(_folder, "dictionary.txt"));
        _suggestions = new SuggestionStore(Path.Combine(_folder, "suggestions.json"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static DictationEdit Edit(string typed, string final) =>
        new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(1)), typed, final, EditAlignment.Changes(typed, final), EditAlignment.WordErrorRate(typed, final));

    private bool Has(string hear, string write) =>
        _dictionary.Entries.Any(e => e.Kind == EntryKind.Correction && e.Hear == hear && e.Write == write);

    [Fact]
    public async Task A_rarely_said_mishearing_is_learnt_from_one_fix()
    {
        string[] history = ["that workflow in Quilla", "lunch at noon"];
        var learner = new EditLearner(_dictionary, _suggestions, () => null, () => true, p => EditLearner.TimesSaid(history, p));

        await learner.LearnAsync(Edit("that workflow in Quilla", "that workflow in Quiller"));

        Has("Quilla", "Quiller").ShouldBeTrue();
        _suggestions.Learnt.ShouldHaveSingleItem().LearntBecause.ShouldBe("you fixed it and hardly ever say it otherwise");
    }

    [Fact]
    public async Task A_word_said_often_waits_for_a_second_fix()
    {
        string[] history = ["it wasn't there", "wasn't me", "that wasn't it", "so the US entity wasn't existent"];
        var learner = new EditLearner(_dictionary, _suggestions, () => null, () => true, p => EditLearner.TimesSaid(history, p));

        await learner.LearnAsync(Edit("so the US entity wasn't existent", "so the US entity was in existent"));

        _dictionary.Entries.ShouldBeEmpty();
        _suggestions.Pending.ShouldHaveSingleItem().Hear.ShouldBe("wasn't");
    }

    [Theory]
    [InlineData("adjustment", "adjustments")]
    [InlineData("Interest", "Interest/")]
    [InlineData("55", "55k")]
    public void Grammar_punctuation_and_numbers_are_not_mishearings(string hear, string write)
    {
        DictionarySuggestions.IsWorthOffering(hear, write).ShouldBeFalse();
    }

    [Fact]
    public void Times_said_counts_whole_words_ignoring_case()
    {
        EditLearner.TimesSaid(["Quilla is good", "quilla", "Quillan", "the Quilla app"], "Quilla").ShouldBe(3);
        EditLearner.TimesSaid(["Complete Audio 2 driver", "complete  audio 2"], "Complete Audio 2").ShouldBe(2);
    }
}
