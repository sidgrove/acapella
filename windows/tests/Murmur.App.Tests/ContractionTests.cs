using Murmur.Speech;
using Shouldly;
using Xunit;

namespace Murmur.AppTests;

/// <summary>The clean-up keeps the speaker's contractions even when the model spells them out.</summary>
public sealed class ContractionTests
{
    [Theory]
    // Real dictations, 29/09/2026.
    [InlineData("We are inconsistent with this as well", "We're inconsistent with this as well", "We're inconsistent with this as well")]
    [InlineData("and when the client is also confirming stuff", "and when the client's also confirming stuff", "and when the client's also confirming stuff")]
    [InlineData("I cannot see all of the account codes. Just make sure nothing is cut off", "I can't see all of the account codes. Just make sure nothing's cut off", "I can't see all of the account codes. Just make sure nothing's cut off")]
    [InlineData("I am here and I will go", "I'm here and I'll go", "I'm here and I'll go")]
    [InlineData("It does not matter", "It doesn't matter", "It doesn't matter")]
    public void A_spelt_out_contraction_goes_back(string cleaned, string reading, string expected) =>
        GeminiCleaner.KeepContractions(cleaned, reading).ShouldBe(expected);

    [Fact]
    public void Words_the_speaker_spelt_out_stay_spelt_out() =>
        GeminiCleaner.KeepContractions("It is fine and it's done", "It is fine and it's done").ShouldBe("It is fine and it's done");

    [Fact]
    public void Either_reading_saying_the_long_form_keeps_it() =>
        GeminiCleaner.KeepContractions("We are late", "We're late", "We are late").ShouldBe("We are late");

    [Fact]
    public void Nothing_to_compare_with_changes_nothing() =>
        GeminiCleaner.KeepContractions("We are late", null, "").ShouldBe("We are late");

    [Fact]
    public void Curly_apostrophes_in_a_reading_count() =>
        GeminiCleaner.KeepContractions("I do not know", "I don’t know").ShouldBe("I don't know");
}
