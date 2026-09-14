using System.Text.Json;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>Send commands are removed and Enter follows only successful delivery.</summary>
public sealed class SpokenSendCommandTests
{
    [Theory]
    [InlineData("send it", "send it", true)]
    [InlineData(" SEND IT! ", "send it", true)]
    [InlineData("Please send it", "send it", false)]
    [InlineData("send it tomorrow", "send it", false)]
    [InlineData("A sentence. Send it.", "send it", false)]
    [InlineData("A paragraph.\nSend it.", "send it", false)]
    [InlineData("Go now.", "go now", true)]
    [InlineData("send it", "", false)]
    public void Standalone_phrase_requires_the_entire_dictation(string text, string phrase, bool expected)
    {
        SpokenSendCommand.IsStandalone(text, phrase).ShouldBe(expected);
    }

    /// <summary>Every one of these was typed into a chat on 9-14 September instead of sending.</summary>
    [Theory]
    [InlineData("Sender")]
    [InlineData("Sender.")]
    [InlineData("Sander?")]
    [InlineData("Sanda")]
    [InlineData("Send a")]
    [InlineData("Send the")]
    [InlineData("Send that.")]
    [InlineData("Sunday.")]
    [InlineData("sent it")]
    public void Mishearings_of_the_phrase_spoken_alone_still_send(string heard)
    {
        SpokenSendCommand.IsStandalone(heard, "send it", SpokenSendCommand.DefaultSendOnlyAliases).ShouldBeTrue();
    }

    [Theory]
    [InlineData("See you on Sunday.")]
    [InlineData("I sent it yesterday.")]
    [InlineData("The sender was unknown.")]
    public void Mishearings_inside_a_sentence_are_ordinary_words(string text)
    {
        SpokenSendCommand.IsStandalone(text, "send it", SpokenSendCommand.DefaultSendOnlyAliases).ShouldBeFalse();
        SpokenSendCommand.Extract(text, "send it").Send.ShouldBeFalse();
    }

    [Fact]
    public void Standalone_aliases_are_separate_from_trailing_ones()
    {
        // "Sunday" alone is a mangled "send it"; "…see you Sunday" at the end of a message is not.
        SpokenSendCommand.Extract("Okay, see you Sunday", "send", "sand").Send.ShouldBeFalse();
        JsonSerializer.Deserialize("{}", SettingsJsonContext.Default.SettingsData)!.SendOnlyAliases.ShouldBe(SpokenSendCommand.DefaultSendOnlyAliases);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_it_does_not_type_or_change_clipboard_or_history(bool enabled)
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(1);
        var injector = new Delivery(true);
        await using var engine = new DictationEngine(capture, hotkey,
            new FakeTranscriber("Send it."), injector, () => []) { InjectText = enabled };
        var copied = "previous transcription";
        var historyEvents = 0;
        var sentEvents = 0;
        engine.CopyTranscriptAsync = text => { copied = text; return Task.CompletedTask; };
        engine.Completed += (_, _) => historyEvents++;
        engine.Sent += (_, _) => sentEvents++;
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
        copied.ShouldBe("previous transcription");
        historyEvents.ShouldBe(0);
        sentEvents.ShouldBe(enabled ? 1 : 0);
        if (enabled) injector.Calls.ShouldHaveSingleItem().ShouldBe("enter");
        else injector.Calls.ShouldBeEmpty();
    }
    [Theory]
    [InlineData("One sentence, sand. Another sentence.", "send", "sand", "One sentence, sand. Another sentence.", false)]
    [InlineData("One paragraph, sand.\nAnother paragraph.", "send", "sand", "One paragraph, sand.\nAnother paragraph.", false)]
    [InlineData("One paragraph.\nAnother paragraph, sand.", "send", "sand", "One paragraph.\nAnother paragraph", true)]
    [InlineData("Hello, scent!", "send", "sand, scent", "Hello", true)]
    [InlineData("Hello, sand.", "send", "", "Hello, sand.", false)]
    [InlineData("Hello, sand.", "", "sand", "Hello, sand.", false)]
    [InlineData("Hello, send now.", "send", "send now", "Hello", true)]
    public void Alternatives_only_match_the_end_of_the_entire_dictation(string input, string word,
        string aliases, string expected, bool send)
    {
        SpokenSendCommand.Extract(input, word, aliases).ShouldBe((expected, send));
    }

    [Fact]
    public async Task Configured_alias_sends_clean_text_and_survives_settings_round_trip()
    {
        var settings = new SettingsData { SendWord = "send", SendWordAliases = "sand, scent" };
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.SettingsData);
        var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData)!;
        restored.SendWordAliases.ShouldBe("sand, scent");
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(1);
        var injector = new Delivery(true);
        await using var engine = new DictationEngine(capture, hotkey,
            new FakeTranscriber("Here is my message, sand."), injector, () => [])
        { SendWord = restored.SendWord, SendWordAliases = restored.SendWordAliases };
        string? copied = null;
        engine.CopyTranscriptAsync = text => { copied = text; return Task.CompletedTask; };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
        injector.Calls.ShouldBe(["Here is my message", "enter"]);
        copied.ShouldBe("Here is my message");
    }
    [Theory]
    [InlineData("Hello, sand.", "send", "Hello", true)]
    [InlineData("Hello, SAND!", "SEND", "Hello", true)]
    [InlineData("Hello, send", "send", "Hello", true)]
    [InlineData("sand", "send", "", true)]
    [InlineData("sand on the beach", "send", "sand on the beach", false)]
    [InlineData("quicksand", "send", "quicksand", false)]
    [InlineData("Hello sand", "blob", "Hello sand", false)]
    [InlineData("Hello sand", "", "Hello sand", false)]
    [InlineData("Here is a sentence, blob", "blob", "Here is a sentence", true)]
    [InlineData("Here is a sentence. BLOB!", "blob", "Here is a sentence.", true)]
    [InlineData("First paragraph.\nSecond paragraph, blob.", "blob", "First paragraph.\nSecond paragraph", true)]
    [InlineData("blob", "blob", "", true)]
    [InlineData("A blobfish", "blob", "A blobfish", false)]
    [InlineData("A blob in the middle", "blob", "A blob in the middle", false)]
    [InlineData("Myblob", "blob", "Myblob", false)]
    [InlineData("Hello, dispatch!", "dispatch", "Hello", true)]
    [InlineData("Hello blob", "", "Hello blob", false)]
    public void Extracts_only_terminal_whole_commands(string input, string word, string expected, bool send)
    {
        SpokenSendCommand.Extract(input, word, word.Equals("send", StringComparison.OrdinalIgnoreCase) ? "sand" : null).ShouldBe((expected, send));
    }

    [Theory]
    [InlineData("Here is a sentence, blob", true, true, "Here is a sentence", 1)]
    [InlineData("Okay, finish up, send it", true, true, "Okay, finish up", 1)]
    [InlineData("Okay, finish up. Send it!", true, true, "Okay, finish up", 1)]
    [InlineData("Okay, finish up, send it", true, false, "Okay, finish up", 0)]
    [InlineData("Okay, finish up, send it", false, true, "Okay, finish up", 0)]
    [InlineData("Please send it tomorrow", true, true, "Please send it tomorrow", 0)]
    [InlineData("Here is a sentence, blob", true, false, "Here is a sentence", 0)]
    [InlineData("Here is a sentence, blob", false, true, "Here is a sentence", 0)]
    [InlineData("Here is a sentence", true, true, "Here is a sentence", 0)]
    [InlineData("blob", true, true, null, 1)]
    [InlineData("blob", false, true, null, 0)]
    public async Task Delivery_removes_command_and_orders_enter(string input, bool enabled,
        bool succeeds, string? expected, int enterCount)
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(1);
        var injector = new Delivery(succeeds);
        await using var engine = new DictationEngine(capture, hotkey,
            new FakeTranscriber(input), injector, () => []) { InjectText = enabled };
        var sentEvents = 0;
        engine.Sent += (_, _) => sentEvents++;
        string? clipboard = null;
        engine.CopyTranscriptAsync = text => { clipboard = text; return Task.CompletedTask; };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
        clipboard.ShouldBe(expected);
        sentEvents.ShouldBe(enterCount);
        injector.Calls.Count(c => c == "enter").ShouldBe(enterCount);
        if (enabled && expected is not null) injector.Calls[0].ShouldBe(expected);
        if (enterCount > 0) injector.Calls[^1].ShouldBe("enter");
        if (!enabled) injector.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Defaults_and_custom_word_survive_settings_serialisation()
    {
        JsonSerializer.Deserialize("{}", SettingsJsonContext.Default.SettingsData)!.SendWord.ShouldBe("blob");
        var json = JsonSerializer.Serialize(new SettingsData { SendWord = "dispatch" }, SettingsJsonContext.Default.SettingsData);
        JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData)!.SendWord.ShouldBe("dispatch");
    }

    private sealed class Delivery(bool succeeds) : ITextInjector
    {
        public List<string> Calls { get; } = [];
        public async ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Calls.Add(text);
            return succeeds;
        }
        public ValueTask<bool> SendAsync(CancellationToken cancellationToken)
        {
            Calls.Add("enter");
            return ValueTask.FromResult(true);
        }
    }
}
