using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>Joining the cleaned pieces of a long dictation without the artefacts of the audio cut.</summary>
public sealed class PieceTextTests
{
    [Theory]
    [InlineData("We've still got a management pack section.", "whereby we've got the inbuilt one.", "We've still got a management pack section whereby we've got the inbuilt one.")]
    [InlineData("P&L, balance sheet, cash flow.", "and have all these useful tools", "P&L, balance sheet, cash flow and have all these useful tools")]
    [InlineData("Latimer Farm, we.", "have the agency commissions.", "Latimer Farm, we have the agency commissions.")]
    [InlineData("It works.", "The next thing.", "It works. The next thing.")]
    [InlineData("See the etc.", "and so on", "See the etc. and so on")]
    [InlineData("Wait for it...", "and then", "Wait for it... and then")]
    [InlineData("", "x", "x")]
    [InlineData("x", "", "x")]
    [InlineData("Cell J.", "3, I think", "Cell J. 3, I think")]
    [InlineData("I want this to be more like the groups mapping", "And also, chart of accounts", "I want this to be more like the groups mapping. And also, chart of accounts")]
    [InlineData("software companies in the accounting industry", "Keep it human-led", "software companies in the accounting industry. Keep it human-led")]
    [InlineData("software companies in the accounting industry", "I think so", "software companies in the accounting industry I think so")]
    [InlineData("software companies in the accounting industry", "I'm sure", "software companies in the accounting industry I'm sure")]
    [InlineData("software companies in the accounting industry", "HMRC said no", "software companies in the accounting industry HMRC said no")]
    [InlineData("THE QUICK BROWN FOX JUMPED", "OVER THE LAZY DOG", "THE QUICK BROWN FOX JUMPED OVER THE LAZY DOG")]
    [InlineData("the live P&L,", "Balance sheet", "the live P&L, Balance sheet")]
    [InlineData("we did it", "and then", "we did it and then")]
    public void A_stop_the_cut_put_there_goes_when_the_sentence_carries_on(string first, string second, string expected) =>
        PieceText.JoinCleaned(first, second).ShouldBe(expected);

    // Seen on 2026-09-22: the continuation opened with "I" or in lower case and the sentence
    // ran on, because only the cleaner of the continuation could know one had ended.
    [Theory]
    [InlineData("there's a clear hierarchy", ". I feel like we should", "there's a clear hierarchy. I feel like we should")]
    [InlineData("resulting in that figure", ". I'm not sure if", "resulting in that figure. I'm not sure if")]
    [InlineData("the annual tabs that you need to amend", ". just bear in mind", "the annual tabs that you need to amend. Just bear in mind")]
    [InlineData("the tabs, basically,", ". Just bear in mind", "the tabs, basically. Just bear in mind")]
    [InlineData("Is that right?", ". I think so", "Is that right? I think so")]
    [InlineData("It works.", ". The next thing", "It works. The next thing")]
    [InlineData("", ". I think so", "I think so")]
    [InlineData("the tabs", ".", "the tabs")]
    [InlineData("wait for it", "... and then", "wait for it ... and then")]
    public void The_cleaner_marks_a_new_sentence_at_the_join(string first, string second, string expected) =>
        PieceText.JoinCleaned(first, second).ShouldBe(expected);

    [Theory]
    [InlineData("making the most effective use of.", "Jev and Gen AI", "making the most effective use of Jev and Gen AI")]
    [InlineData("it's one of.", "The best we've had", "it's one of the best we've had")]
    [InlineData("so that we can see whether.", "It works", "so that we can see whether it works")]
    [InlineData("I'll log in.", "The next thing", "I'll log in. The next thing")]
    [InlineData("We went with plan A.", "The next thing", "We went with plan A. The next thing")]
    [InlineData("That's what it is.", "Anyway", "That's what it is. Anyway")]
    public void A_stop_after_a_word_no_sentence_ends_on_goes(string first, string second, string expected) =>
        PieceText.JoinCleaned(first, second).ShouldBe(expected);

    [Theory]
    [InlineData(". I think so", "I think so")]
    [InlineData("I think so", "I think so")]
    [InlineData("... and then", "... and then")]
    public void The_new_sentence_mark_is_left_out_of_word_counts(string cleaned, string expected) =>
        PieceText.WithoutNewSentenceMark(cleaned).ShouldBe(expected);

    [Theory]
    [InlineData("management pack section.", "management pack section")]
    [InlineData("we do it e.g.", "we do it e.g.")]
    [InlineData("wait for it...", "wait for it...")]
    [InlineData("is that right?", "is that right?")]
    [InlineData("no stop", "no stop")]
    public void The_artificial_stop_is_removed_before_the_cleaner_sees_a_piece(string raw, string expected) =>
        PieceText.WithoutArtificialStop(raw).ShouldBe(expected);
}
