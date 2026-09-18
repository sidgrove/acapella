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
    public void A_stop_the_cut_put_there_goes_when_the_sentence_carries_on(string first, string second, string expected) =>
        PieceText.JoinCleaned(first, second).ShouldBe(expected);

    [Theory]
    [InlineData("management pack section.", "management pack section")]
    [InlineData("we do it e.g.", "we do it e.g.")]
    [InlineData("wait for it...", "wait for it...")]
    [InlineData("is that right?", "is that right?")]
    [InlineData("no stop", "no stop")]
    public void The_artificial_stop_is_removed_before_the_cleaner_sees_a_piece(string raw, string expected) =>
        PieceText.WithoutArtificialStop(raw).ShouldBe(expected);
}
