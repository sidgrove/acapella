using Murmur.Speech;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The speech model's unknown token never reaches the page.</summary>
public sealed class TranscriptNormaliserTests
{
    [Theory]
    [InlineData("we put the P<unk>L and the variance column", "we put the P&L and the variance column")]
    [InlineData("R<unk>D tax credits", "R&D tax credits")]
    [InlineData("hello <unk> world", "hello world")]
    [InlineData("hello<unk>", "hello")]
    [InlineData("no token here", "no token here")]
    public void Unknown_tokens_become_an_ampersand_or_nothing(string input, string expected) =>
        TranscriptNormaliser.Apply(input).ShouldBe(expected);
}
