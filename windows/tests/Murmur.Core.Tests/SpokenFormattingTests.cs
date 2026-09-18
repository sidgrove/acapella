using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The rules layer that makes local-only dictation feel finished.</summary>
public sealed class SpokenFormattingTests
{
    [Theory]
    [InlineData("Um, so can you send me the numbers?", "So can you send me the numbers?")]
    [InlineData("I think, er, it's fine.", "I think, it's fine.")]
    [InlineData("Hmm. Let me see.", "Let me see.")]
    [InlineData("Summer is here.", "Summer is here.")]
    [InlineData("The drummer was late.", "The drummer was late.")]
    [InlineData("It's 5 mm wide.", "It's 5 mm wide.")]
    [InlineData("It's 5mm wide.", "It's 5mm wide.")]
    [InlineData("She went to the ER last night.", "She went to the ER last night.")]
    [InlineData("Mm, I think so.", "I think so.")]
    public void Fillers_go_but_real_words_stay(string input, string expected) =>
        SpokenFormatting.Apply(input).ShouldBe(expected);

    [Theory]
    [InlineData("Send it by Friday. Scratch that. Send it by Thursday.", "Send it by Thursday.")]
    [InlineData("Buy milk, scratch that, buy water", "Buy water")]
    [InlineData("First point. Second point, delete that.", "First point.")]
    public void Scratch_that_removes_the_clause_before_it(string input, string expected) =>
        SpokenFormatting.Apply(input).ShouldBe(expected);

    [Theory]
    [InlineData("Thanks Dave new line see you Monday", "Thanks Dave\nSee you Monday")]
    [InlineData("Thanks Dave. New line. See you Monday.", "Thanks Dave.\nSee you Monday.")]
    [InlineData("Intro, new paragraph, body", "Intro\n\nBody")]
    public void Breaks_become_line_breaks_and_recapitalise(string input, string expected) =>
        SpokenFormatting.Apply(input).ShouldBe(expected);

    [Theory]
    [InlineData("Are you coming question mark", "Are you coming?")]
    [InlineData("One comma two comma three full stop", "One, two, three.")]
    [InlineData("Twenty hyphen five", "Twenty-five")]
    [InlineData("He said open quote no close quote", "He said “no”")]
    public void Spoken_punctuation_becomes_marks(string input, string expected) =>
        SpokenFormatting.Apply(input).ShouldBe(expected);

    /// <summary>
    /// A week of real history: "period" was the noun every one of sixteen times and
    /// "dash" was wrong both times it fired. Neither is a command in this en-GB product.
    /// </summary>
    [Theory]
    [InlineData("PAYE reference period reference.")]
    [InlineData("Period reference.")]
    [InlineData("What is the VAT period end for this client?")]
    [InlineData("Don't use m dash dashes please.")]
    [InlineData("Wait dash really")]
    public void Period_and_dash_are_ordinary_words(string input) =>
        SpokenFormatting.Apply(input).ShouldBe(input);

    [Fact]
    public void Full_stop_still_works_next_to_the_word_period() =>
        SpokenFormatting.Apply("The accounting period ends on the 31st of March. Full stop.").ShouldBe("The accounting period ends on the 31st of March.");

    /// <summary>Commands that describe a thing, or tell someone else what to do, are left alone.</summary>
    [Theory]
    [InlineData("Can you delete that file and push again?")]
    [InlineData("Please strike that deal off the list.")]
    [InlineData("Can you add a new line to the invoice for the consultancy fee?")]
    [InlineData("Add a new line item for the prepayment.")]
    [InlineData("What the f- is that?")]
    public void Command_words_inside_a_clause_are_not_commands(string input) =>
        SpokenFormatting.Apply(input).ShouldBe(input);

    [Theory]
    [InlineData("I use e.g. the Xero API. Um, and sidgrove.com is the site.", "I use e.g. the Xero API. And sidgrove.com is the site.")]
    [InlineData("Um, see Perplexity.ai for that", "See Perplexity.ai for that")]
    [InlineData("Wait for it... um okay", "Wait for it... okay")]
    [InlineData("Um, what is the full stop?", "What is the?")]
    public void Tidying_respects_tokens_ellipses_and_mixed_marks(string input, string expected) =>
        SpokenFormatting.Apply(input).ShouldBe(expected);

    [Fact]
    public void Everything_can_be_switched_off()
    {
        const string text = "Um, new line, comma";
        SpokenFormatting.Apply(text, commands: false, fillers: false).ShouldBe(text);
    }

    [Fact]
    public void Plain_prose_is_untouched()
    {
        const string text = "Hi Dave, just checking in on the Sidders app. It's looking really good now, I think.";
        SpokenFormatting.Apply(text).ShouldBe(text);
    }
}
