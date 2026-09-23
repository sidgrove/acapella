using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>Two dictations into one field read on from each other; anything else gets no prefix.</summary>
public sealed class ContinuationRuleTests
{
    private static readonly DateTimeOffset Then = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    private static TypedDelivery Previous(string text, bool stopDropped = true, string? target = "A/1", bool enter = false) =>
        new(text, stopDropped, target, KeyPressesAfter: 5, Then, enter);

    [Fact]
    public void A_new_sentence_after_a_dropped_stop_gets_the_stop_back() =>
        ContinuationRule.Prefix(Previous("we just need to be prudent"), "And have good understanding", "A/1", 5, Then.AddSeconds(9), "we just need to be prudent").ShouldBe(". ");

    [Fact]
    public void A_lower_case_continuation_gets_a_space() =>
        ContinuationRule.Prefix(Previous("the bottom line"), "by financing literally", "A/1", 5, Then.AddSeconds(9), "the bottom line").ShouldBe(" ");

    [Fact]
    public void A_question_kept_its_mark_so_only_a_space_is_needed() =>
        ContinuationRule.Prefix(Previous("how's that going?", stopDropped: false), "The Claude reset", "A/1", 5, Then.AddSeconds(9), "how's that going?").ShouldBe(" ");

    [Theory]
    [InlineData("B/2", 5, 9, false)]     // a different control
    [InlineData("A/1", 6, 9, false)]     // the user typed in between
    [InlineData("A/1", 5, 600, false)]   // ten minutes later
    [InlineData("A/1", 5, 9, true)]      // the last one pressed Enter
    public void Nothing_is_typed_when_the_field_or_the_moment_has_moved_on(string target, long presses, int seconds, bool enter) =>
        ContinuationRule.Prefix(Previous("done", enter: enter), "Next", target, presses, Then.AddSeconds(seconds), "done").ShouldBe(string.Empty);

    [Fact]
    public void The_first_dictation_of_a_session_has_nothing_to_join() =>
        ContinuationRule.Prefix(null, "Hello", "A/1", 0, Then, null).ShouldBe(string.Empty);

    [Fact]
    public void An_unknown_target_is_never_joined() =>
        ContinuationRule.Prefix(Previous("done", target: null), "Next", null, 5, Then.AddSeconds(1), "done").ShouldBe(string.Empty);

    [Theory]
    [InlineData("done ", "Next")]
    [InlineData("done", "\nNext")]
    [InlineData("done", "- a bullet")]
    public void Whitespace_and_punctuation_at_the_join_are_left_to_the_user(string previous, string next) =>
        ContinuationRule.Prefix(Previous(previous), next, "A/1", 5, Then.AddSeconds(1), previous).ShouldBe(string.Empty);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a different message")]
    public void A_cleared_replaced_or_unreadable_field_is_not_joined(string? beforeCaret) =>
        ContinuationRule.Prefix(Previous("previous thought"), "Okay", "A/1", 5, Then.AddSeconds(1), beforeCaret).ShouldBe(string.Empty);
}
