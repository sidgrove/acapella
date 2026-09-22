using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>A dictation started part way through a sentence loses its capital; nothing else changes.</summary>
public sealed class CaretCaseTests
{
    [Theory]
    [InlineData("And then we left", "I was going to", "and then we left")]
    [InlineData("And then we left", "I was going to ", "and then we left")]
    [InlineData("The plan is fine", "Well,", "the plan is fine")]
    [InlineData("So that works", "one thing; ", "so that works")]
    [InlineData("It's fine", "as far as I can see - ", "it's fine")]
    [InlineData("It’s fine", "as far as I can see", "it’s fine")]
    public void Mid_sentence_drops_the_capital_of_a_common_word(string text, string before, string expected) =>
        CaretCase.Apply(text, before).ShouldBe(expected);

    [Theory]
    [InlineData("That was it.")]
    [InlineData("That was it?")]
    [InlineData("That was it!")]
    [InlineData("That was it.\"")]
    [InlineData("That was it. ")]
    [InlineData("Dear Sam,\n")]
    [InlineData("Dear Sam,\r\n")]
    [InlineData("Note:")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_sentence_start_keeps_the_capital(string before) =>
        CaretCase.Apply("And then we left", before).ShouldBe("And then we left");

    [Fact]
    public void An_unknown_context_keeps_the_capital() =>
        CaretCase.Apply("And then we left", null).ShouldBe("And then we left");

    [Theory]
    [InlineData("I think so")]
    [InlineData("I'm not sure")]
    [InlineData("London is cold")]
    [InlineData("Sidgrove said so")]
    [InlineData("Will said no")]
    [InlineData("May is busy")]
    [InlineData("AI is here")]
    [InlineData("iPhone sales")]
    [InlineData("Went to the shop")]
    [InlineData("2 things")]
    public void Names_I_acronyms_and_words_off_the_list_keep_the_capital(string text) =>
        CaretCase.Apply(text, "I was going to").ShouldBe(text);

    [Fact]
    public void Text_that_is_already_lower_case_is_returned_as_it_came() =>
        ReferenceEquals(CaretCase.Apply("and then", "I was going to"), "and then").ShouldBeTrue();

    // The engine: read at key-down, applied at delivery, never waited for.

    private static async Task<List<string>> DictateAsync(string spoken, RecordingTextInjector injector, bool matchCase = true)
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber(spoken), injector, () => [])
        {
            MatchCaseToCaret = matchCase,
        };

        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
        return injector.Injected;
    }

    [Fact]
    public async Task The_engine_types_a_lower_case_start_mid_sentence()
    {
        var injector = new RecordingTextInjector { CaretText = "I was going to" };
        (await DictateAsync("And then we left", injector)).ShouldBe(["and then we left"]);
    }

    [Fact]
    public async Task The_engine_keeps_the_capital_at_a_sentence_start()
    {
        var injector = new RecordingTextInjector { CaretText = "That was it." };
        (await DictateAsync("And then we left", injector)).ShouldBe(["And then we left"]);
    }

    [Fact]
    public async Task The_engine_keeps_the_capital_where_the_app_cannot_say()
    {
        var injector = new RecordingTextInjector();
        (await DictateAsync("And then we left", injector)).ShouldBe(["And then we left"]);
    }

    [Fact]
    public async Task The_engine_keeps_the_capital_when_the_setting_is_off()
    {
        var injector = new RecordingTextInjector { CaretText = "I was going to" };
        (await DictateAsync("And then we left", injector, matchCase: false)).ShouldBe(["And then we left"]);
    }

    /// <summary>An app that never answers must not hold the dictation: the text is typed as it was.</summary>
    [Fact]
    public async Task A_stuck_read_does_not_delay_delivery()
    {
        var injector = new RecordingTextInjector { CaretText = "I was going to", CaretDelay = TimeSpan.FromSeconds(30) };
        var clock = System.Diagnostics.Stopwatch.StartNew();

        (await DictateAsync("And then we left", injector)).ShouldBe(["And then we left"]);

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }
}
