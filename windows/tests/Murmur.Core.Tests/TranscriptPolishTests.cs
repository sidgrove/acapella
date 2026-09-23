using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The rules that tidy a transcript without rewording it.</summary>
public sealed class TranscriptPolishTests
{
    [Theory]
    [InlineData("Can you send me the Q2 numbers.", "Can you send me the Q2 numbers")]
    [InlineData("Sounds good.", "Sounds good")]
    [InlineData("Sounds good.  ", "Sounds good")]
    public void A_lone_sentence_loses_its_full_stop(string input, string expected) =>
        TranscriptPolish.DropTrailingFullStopIfSingleSentence(input).ShouldBe(expected);

    [Theory]
    [InlineData("First thing. Second thing.")]
    [InlineData("Is that right?")]
    [InlineData("Brilliant!")]
    [InlineData("Wait for it...")]
    [InlineData("No punctuation at all")]
    [InlineData("")]
    public void Prose_questions_exclamations_and_ellipses_are_left_alone(string input) =>
        TranscriptPolish.DropTrailingFullStopIfSingleSentence(input).ShouldBe(input);

    [Fact]
    public void The_rule_can_be_switched_off()
    {
        TranscriptPolish.Apply("Sounds good.", dropSingleSentenceFullStop: false).ShouldBe("Sounds good.");
        TranscriptPolish.Apply("Sounds good.", dropSingleSentenceFullStop: true).ShouldBe("Sounds good");
    }

    [Theory]
    [InlineData("Okay, just git pull, and also, I don't know", "Okay, just git pull and also, I don't know")]
    [InlineData("accounts receivable, accounts payable, and then obviously", "accounts receivable, accounts payable and then obviously")]
    [InlineData("I'm overreacting, and I think", "I'm overreacting and I think")]
    [InlineData("Sand, andouille", "Sand, andouille")]
    [InlineData("black and white", "black and white")]
    [InlineData("First, And second", "First And second")]
    public void The_comma_before_and_goes(string input, string expected) =>
        TranscriptPolish.RemoveCommaBeforeAnd(input).ShouldBe(expected);
}

/// <summary>American spellings from the speech model, written the British way.</summary>
public sealed class BritishSpellingTests
{
    [Theory]
    [InlineData("Can you optimize this for LinkedIn?", "Can you optimise this for LinkedIn?")]
    [InlineData("Analyze", "Analyse")]
    [InlineData("Favor Brazil", "Favour Brazil")]
    [InlineData("the realization was organized", "the realisation was organised")]
    [InlineData("We are minimizing behavior", "We are minimising behaviour")]
    [InlineData("the color center is gray", "the colour centre is grey")]
    [InlineData("What size prize did they seize?", "What size prize did they seize?")]
    [InlineData("set background-color to red", "set background-color to red")]
    [InlineData("call colorize.js and Optimizer.run", "call colorize.js and Optimizer.run")]
    [InlineData("a computer program", "a computer program")]
    [InlineData("recognize and REALIZE", "recognise and REALISE")]
    public void Only_whole_prose_words_change(string input, string expected) =>
        BritishSpellings.Apply(input).ShouldBe(expected);
}

/// <summary>The clean-up guard: a model that summarises never reaches the text field.</summary>
public sealed class CleanupGuardTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("one", true)]
    [InlineData("one two", true)]
    public void Every_non_empty_utterance_is_worth_cleaning(string input, bool expected)
    {
        CleanupGuard.IsWorthCleaning(input).ShouldBe(expected);
    }

    [Fact]
    public void A_short_question_is_not_allowed_to_grow_into_an_answer()
    {
        CleanupGuard.IsPlausible("does it have a game", "Does it have a game?").ShouldBeTrue();
        CleanupGuard.IsPlausible("does it have a game", "No, it does not have a game.").ShouldBeFalse();
    }

    [Theory]
    [InlineData("um so can you uh send me the the Q2 numbers by friday", "Can you send me the Q2 numbers by Friday", true)]
    [InlineData("um so hello there", "Hello there", true)]
    [InlineData("I think this is fine and we should go ahead with the plan as discussed", "Go ahead.", false)]
    [InlineData("one two one two", "", false)]
    [InlineData("one two one two", null, false)]
    [InlineData("sounds good", "Sounds good, I will send it over to you first thing tomorrow morning", false)]
    public void Rewrites_are_rejected(string raw, string? cleaned, bool plausible) =>
        CleanupGuard.IsPlausible(raw, cleaned).ShouldBe(plausible);
}
