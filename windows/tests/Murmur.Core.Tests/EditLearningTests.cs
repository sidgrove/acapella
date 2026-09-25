using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>Finding a typed dictation in its field again, and what the user changed.</summary>
public sealed class EditAlignmentTests
{
    [Fact]
    public void The_dictation_is_found_among_the_text_around_it_with_the_users_fix()
    {
        var field = "Earlier message. Can we run Impeccable over the serif headings please? And more I typed";
        var found = EditAlignment.Find("Can we run Impeccable over the Sarif headings please?", field);

        found.ShouldBe("Can we run Impeccable over the serif headings please?");
    }

    [Fact]
    public void Gone_from_the_field_is_null()
    {
        EditAlignment.Find("Can we run Impeccable over the Sarif headings please?", "Something else entirely in this box now").ShouldBeNull();
        EditAlignment.Find("anything", string.Empty).ShouldBeNull();
    }

    [Fact]
    public void When_it_appears_twice_the_later_one_is_taken()
    {
        var field = "Sounds good to me. Sounds good to me!";
        var found = EditAlignment.Find("Sounds good to me.", field);

        found.ShouldBe("Sounds good to me!");
    }

    [Fact]
    public void Replaced_words_are_listed_and_punctuation_or_a_sentence_capital_are_not()
    {
        var changes = EditAlignment.Changes(
            "Yeah the Sarif headings on get hub look fine, and Cork Tax is due",
            "yeah, the serif headings on GitHub look fine and Corp Tax is due.");

        changes.ShouldBe([new WordChange("Sarif", "serif"), new WordChange("get hub", "GitHub"), new WordChange("Cork", "Corp")]);
    }

    [Fact]
    public void A_case_change_that_makes_a_name_is_a_change()
    {
        EditAlignment.Changes("push it to github", "push it to GitHub").ShouldBe([new WordChange("github", "GitHub")]);
    }

    [Fact]
    public void Words_only_added_or_only_removed_are_not_changes_but_count_as_errors()
    {
        EditAlignment.Changes("send me the numbers", "send me the March numbers").ShouldBeEmpty();
        EditAlignment.WordErrorRate("send me the numbers", "send me the March numbers").ShouldBe(0.2, 0.001);
        EditAlignment.WordErrorRate("Left alone.", "left alone").ShouldBe(0);
    }
}

/// <summary>Which hand fixes become dictionary suggestions, and how they are kept.</summary>
public sealed class DictionarySuggestionTests
{
    [Theory]
    [InlineData("Sarif", "serif")]
    [InlineData("Gev", "Jev")]
    [InlineData("Cork Tax", "Corp Tax")]
    [InlineData("explanation mark", "exclamation mark")]
    [InlineData("Get pole", "git pull")]
    [InlineData("github", "GitHub")]
    public void Sound_alike_fixes_are_offered(string hear, string write)
    {
        DictionarySuggestions.IsWorthOffering(hear, write).ShouldBeTrue();
    }

    [Theory]
    [InlineData("want", "need")]
    [InlineData("big", "large")]
    [InlineData("we could go with the first option", "we could go with the second option")]
    [InlineData("twelve", "12")]
    public void Rewrites_are_not(string hear, string write)
    {
        DictionarySuggestions.IsWorthOffering(hear, write).ShouldBeFalse();
    }

    [Fact]
    public void An_ordinary_word_or_one_already_in_the_dictionary_is_not_offered()
    {
        var entries = DictionaryFile.Parse("Sarif -> serif");
        var offers = DictionarySuggestions.From([new WordChange("Sarif", "serif"), new WordChange("see", "serif"), new WordChange("Gev", "Jev")], entries);

        offers.ShouldBe([("Gev", "Jev")]);
    }

    [Fact]
    public void The_example_is_the_fix_with_a_few_words_either_side()
    {
        var example = DictionarySuggestions.Example("Okay so I think we should run Impeccable over the serif headings on the pricing page before we ship it", "serif", around: 20);

        example.ShouldNotBeNull().ShouldContain("serif headings");
        example.ShouldStartWith("…");
        example.ShouldEndWith("…");
    }

    [Fact]
    public void A_repeated_fix_is_counted_a_dismissed_one_stays_dismissed_and_both_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-suggestions-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SuggestionStore(path);
            var at = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.FromHours(1));
            store.Offer("Sarif", "serif", "the serif headings", at);
            store.Offer("sarif", "serif", null, at.AddMinutes(1));
            store.Offer("Gev", "Jev", null, at);
            store.Dismiss(store.Pending.Single(s => s.Hear == "Gev").Id);
            store.Offer("Gev", "Jev", null, at.AddMinutes(2));

            var reopened = new SuggestionStore(path);
            var only = reopened.Pending.ShouldHaveSingleItem();
            only.Hear.ShouldBe("Sarif");
            only.Count.ShouldBe(2);
            only.Example.ShouldBe("the serif headings");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>Reading the field back after typing.</summary>
public sealed class EditWatcherTests
{
    private static EditWatcher Fast(ITextInjector injector) => new(injector)
    {
        FirstLook = TimeSpan.FromMilliseconds(10),
        Interval = TimeSpan.FromMilliseconds(20),
        Duration = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public async Task A_fix_made_before_sending_is_reported_when_the_text_leaves_the_field()
    {
        var injector = new RecordingTextInjector { FocusTarget = "chat", AroundCaretText = "Run it over the Sarif headings" };
        var watcher = Fast(injector);
        DictationEdit? edit = null;
        watcher.Finished += (_, e) => edit = e;

        watcher.Watch(DateTimeOffset.UnixEpoch, "Run it over the Sarif headings", "chat");
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= 2)).ShouldBeTrue();
        injector.AroundCaretText = "Run it over the serif headings";
        var reads = injector.AroundCaretReads;
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= reads + 2)).ShouldBeTrue();
        injector.AroundCaretText = string.Empty;   // sent
        await watcher.Current.WaitAsync(Wait.Timeout);

        edit.ShouldNotBeNull();
        edit.Final.ShouldBe("Run it over the serif headings");
        edit.Changes.ShouldBe([new WordChange("Sarif", "serif")]);
        edit.IsEdited.ShouldBeTrue();
    }

    [Fact]
    public async Task Moving_to_another_field_ends_the_watch_and_keeps_what_was_seen()
    {
        var injector = new RecordingTextInjector { FocusTarget = "chat", AroundCaretText = "Left exactly as typed" };
        var watcher = Fast(injector);
        DictationEdit? edit = null;
        watcher.Finished += (_, e) => edit = e;

        watcher.Watch(DateTimeOffset.UnixEpoch, "Left exactly as typed", "chat");
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= 1)).ShouldBeTrue();
        injector.FocusTarget = "elsewhere";
        await watcher.Current.WaitAsync(Wait.Timeout);

        edit.ShouldNotBeNull().IsEdited.ShouldBeFalse();
    }

    [Fact]
    public async Task The_next_key_down_stops_it_at_once()
    {
        var injector = new RecordingTextInjector { FocusTarget = "chat", AroundCaretText = "hello there" };
        var watcher = new EditWatcher(injector) { FirstLook = TimeSpan.FromMilliseconds(10), Interval = TimeSpan.FromMinutes(1) };
        DictationEdit? edit = null;
        watcher.Finished += (_, e) => edit = e;

        watcher.Watch(DateTimeOffset.UnixEpoch, "hello there", "chat");
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= 1)).ShouldBeTrue();
        watcher.Stop();
        await watcher.Current.WaitAsync(Wait.Timeout);

        edit.ShouldNotBeNull().Final.ShouldBe("hello there");
    }

    [Fact]
    public async Task A_field_that_cannot_be_told_apart_is_not_watched()
    {
        var injector = new RecordingTextInjector { AroundCaretText = "hello" };
        var watcher = Fast(injector);

        watcher.Watch(DateTimeOffset.UnixEpoch, "hello", target: null);
        await Task.Delay(100);

        injector.AroundCaretReads.ShouldBe(0);
    }

    [Fact]
    public async Task The_engine_watches_what_it_typed_and_not_a_spoken_send()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector { FocusTarget = "chat" };
        var watcher = Fast(injector);
        DictationEdit? edit = null;
        watcher.Finished += (_, e) => edit = e;
        var capture = FakeAudioCapture.Tone(0.8);

        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("the local words"), injector, () => []) { Edits = watcher };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => injector.Injected.Count > 0);
        injector.AroundCaretText = "Hi. " + injector.Injected[0].Replace("local", "loco", StringComparison.Ordinal);
        (await Wait.UntilAsync(() => injector.AroundCaretReads >= 2)).ShouldBeTrue();
        watcher.Stop();
        await watcher.Current.WaitAsync(Wait.Timeout);

        edit.ShouldNotBeNull().Changes.ShouldBe([new WordChange("local", "loco")]);
    }
}

/// <summary>Updating a history record in place.</summary>
public sealed class TranscriptUpdateTests
{
    [Fact]
    public void An_edit_is_saved_onto_its_record_and_read_back()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-history-{Guid.NewGuid():N}.jsonl");
        try
        {
            var store = new TranscriptStore(path);
            var at = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.FromHours(1));
            store.Add(new TranscriptRecord { At = at, Text = "the Sarif headings" });
            store.Add(new TranscriptRecord { At = at.AddSeconds(5), Text = "something else" });

            store.Update(r => r.At == at, r => r with { EditChecked = true, EditedText = "the serif headings" }).ShouldBeTrue();
            store.Update(r => r.At == at.AddDays(1), r => r).ShouldBeFalse();

            var reopened = new TranscriptStore(path).Records;
            reopened.Count.ShouldBe(2);
            reopened.Single(r => r.At == at).EditedText.ShouldBe("the serif headings");
            reopened.Single(r => r.At != at).EditChecked.ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>The quality report reads what the user did with each dictation.</summary>
public sealed class QualityReportTests
{
    [Fact]
    public void Edited_and_untouched_dictations_are_scored_and_unread_ones_only_counted()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(1));
        TranscriptRecord[] records =
        [
            new() { At = now.AddHours(-1), Text = "the Sarif headings look fine", EditChecked = true, EditedText = "the serif headings look fine", ProcessingSeconds = 0.8 },
            new() { At = now.AddHours(-2), Text = "left exactly as it was typed", EditChecked = true, ProcessingSeconds = 0.6 },
            new() { At = now.AddHours(-3), Text = "sent before it could be read", ProcessingSeconds = 1.0 },
            new() { At = now.AddDays(-9), Text = "too old to count", EditChecked = true, EditedText = "far too old" },
        ];

        var (checkedCount, edited, wer) = QualityReport.Accuracy([.. records.Take(3)]);
        checkedCount.ShouldBe(2);
        edited.ShouldBe(1);
        wer.ShouldBe(1.0 / 11, 0.001);   // one word wrong of the eleven the user left

        var report = QualityReport.Build(records, now, days: 7);
        report.ShouldContain("All: 3 dictations");
        report.ShouldContain("Edited by you: 1 of 2 (50%)");
        report.ShouldContain("Sarif -> serif");
        report.ShouldNotContain("too old");
    }
}
