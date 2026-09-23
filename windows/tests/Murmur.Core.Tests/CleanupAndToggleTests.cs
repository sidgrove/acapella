using System.Net;
using System.Text;
using System.Text.Json;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Speech;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The generative tier, against a fake Gemini, and the engine's use of it.</summary>
public sealed class GeminiCleanerTests
{
    private static GeminiCleaner Build(FakeGemini server, string? key = "test-key") =>
        new(() => key, null, server);

    [Fact]
    public async Task Sends_the_instructions_and_text_and_returns_the_reply()
    {
        var server = new FakeGemini(reply: "Can you send me the Q2 numbers by Thursday");
        using var cleaner = Build(server);

        var cleaned = await cleaner.CleanAsync("um can you uh send me the the Q2 numbers by friday scratch that by thursday", CancellationToken.None);

        cleaned.ShouldBe("Can you send me the Q2 numbers by Thursday");
        // The full URL, not a substring: the old (base, relative) construction produced a URI
        // whose *scheme* was "gemini-2.5-flash", which contained the substring and passed.
        server.LastRequestUri!.ToString().ShouldBe("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
        server.LastRequestUri.Scheme.ShouldBe("https");
        server.LastKeyHeader.ShouldBe("test-key");
        server.LastBody.ShouldContain("systemInstruction");
        server.LastBody.ShouldContain("scratch that");
        server.LastBody.ShouldContain("\"thinkingBudget\":0");
    }

    [Fact]
    public async Task No_key_means_no_call_and_null()
    {
        var server = new FakeGemini(reply: "never");
        using var cleaner = Build(server, key: null);

        // Environment may carry a key on a developer machine; only assert when it does not.
        if (Environment.GetEnvironmentVariable(GeminiCleaner.ApiKeyEnvironmentVariable) is { Length: > 0 }) return;

        (await cleaner.CleanAsync("hello", CancellationToken.None)).ShouldBeNull();
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task An_error_status_returns_null_and_keeps_the_message()
    {
        var server = new FakeGemini(status: HttpStatusCode.BadRequest, rawBody: "{\"error\":{\"message\":\"API key not valid\"}}");
        using var cleaner = Build(server);

        (await cleaner.CleanAsync("hello", CancellationToken.None)).ShouldBeNull();
        cleaner.LastError.ShouldNotBeNull().ShouldContain("API key not valid");
    }

    [Fact]
    public async Task An_empty_candidate_list_returns_null()
    {
        var server = new FakeGemini(rawBody: "{\"candidates\":[]}");
        using var cleaner = Build(server);

        (await cleaner.CleanAsync("hello", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_dictionary_goes_to_the_model_as_vocabulary()
    {
        var server = new FakeGemini(reply: "git pull and check Xero");
        using var cleaner = new GeminiCleaner(() => "test-key", null, server, vocabulary: () => ["Xero", "git pull"]);

        await cleaner.CleanAsync("get pool and check zero", CancellationToken.None);

        server.LastBody.ShouldContain("own word list");
        server.LastBody.ShouldContain("Xero, git pull");
    }

    [Fact]
    public async Task An_empty_dictionary_adds_nothing_to_the_prompt()
    {
        var server = new FakeGemini(reply: "x");
        using var cleaner = Build(server);

        await cleaner.CleanAsync("hello there", CancellationToken.None);

        server.LastBody.ShouldNotContain("write it exactly as listed");
    }

    [Fact]
    public async Task A_piece_is_framed_as_possibly_unfinished_and_a_tail_is_not()
    {
        var server = new FakeGemini(reply: "x");
        using var cleaner = Build(server);

        await cleaner.CleanPieceAsync("we've still got a section", null, CancellationToken.None);
        server.LastBody.ShouldContain("may stop mid-sentence");

        await cleaner.CleanAsync("and nothing else", "We've still got a section", CancellationToken.None);
        server.LastBody.ShouldNotContain("may stop mid-sentence");
        server.LastBody.ShouldContain("earlier part of this dictation");
    }

    [Fact]
    public async Task No_key_is_named_as_the_reason()
    {
        if (Environment.GetEnvironmentVariable(GeminiCleaner.ApiKeyEnvironmentVariable) is { Length: > 0 }) return;
        using var cleaner = Build(new FakeGemini(reply: "never"), key: null);

        (await cleaner.CleanAsync("hello there", CancellationToken.None)).ShouldBeNull();
        cleaner.LastError.ShouldBe("no API key");
    }

    [Fact]
    public void The_prompt_keeps_period_as_a_word_and_swearing_as_the_speakers_own()
    {
        GeminiCleaner.Instructions.ShouldContain("\"Period\" is always a word");
        GeminiCleaner.Instructions.ShouldContain("Swearing and intensifiers are the speaker's words");
        GeminiCleaner.Instructions.ShouldContain("Never write an em dash or an en dash");
    }

    /// <summary>A stand-in for the Generative Language endpoint.</summary>
    private sealed class FakeGemini : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeGemini(string reply) : this(HttpStatusCode.OK, Wrap(reply)) { }

        public FakeGemini(HttpStatusCode status = HttpStatusCode.OK, string rawBody = "{}")
        {
            _status = status;
            _body = rawBody;
        }

        public int Requests { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastKeyHeader { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        private static string Wrap(string reply) =>
            JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = reply } } } } } });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestUri = request.RequestUri;
            LastKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.First() : null;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}

/// <summary>How the engine uses a cleaner, and the tap-to-toggle mode.</summary>
public sealed class EngineCleanupAndToggleTests
{
    private static async Task DrainAndReleaseAsync(FakeHotkeySource hotkey, DictationEngine engine, FakeAudioCapture capture)
    {
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
    }

    [Fact]
    public async Task Cleaned_text_is_typed_and_recorded_when_the_tier_is_on()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        DictationResult? completed = null;

        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("um so hello there, how are you."), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = new StubCleaner("Hello there, how are you"),
        };
        engine.Completed += (_, r) => completed = r;

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        injector.Injected.ShouldBe(["Hello there, how are you"]);
        completed.ShouldNotBeNull().CleanedBy.ShouldBe("stub");
    }

    [Fact]
    public async Task A_single_word_is_sent_to_the_cleaner_when_the_tier_is_on()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var cleaner = new StubCleaner("Jev");
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("Jeff"), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = cleaner,
        };

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        cleaner.Calls.ShouldBe(1);
        injector.Injected.ShouldBe(["Jev"]);
    }

    /// <summary>
    /// The cleaner is shown the spoken commands as words, so "period", "full stop" and
    /// "new line" are judged in context; the local rules' reading is only the fallback.
    /// </summary>
    [Fact]
    public async Task The_cleaner_sees_commands_as_words_and_the_fallback_applies_them()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var seen = new RecordingCleaner(reply: null);
        const string raw = "One comma two full stop and the period ends in March.";

        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber(raw), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = seen,
            FullStops = TrailingFullStop.Never,
        };

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        seen.Texts.ShouldHaveSingleItem().ShouldBe(raw);
        injector.Injected.ShouldBe(["One, two. And the period ends in March"]);
    }

    [Fact]
    public async Task History_is_written_after_the_text_is_typed()
    {
        var hotkey = new FakeHotkeySource();
        var order = new List<string>();
        var injector = new OrderInjector(order);
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello there"), injector, () => []);
        engine.Completed += (_, _) => order.Add("completed");
        engine.CopyTranscriptAsync = _ => { order.Add("copied"); return Task.CompletedTask; };

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        // The copy first, so a paste never has the clipboard swapped under it; the
        // history last, so its list rebuild never delays the typing.
        order.ShouldBe(["copied", "inject", "completed"]);
    }

    [Fact]
    public async Task House_style_and_british_spellings_apply_to_what_the_cleaner_returns()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("we should optimize the report, and then send it over"), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = new StubCleaner("We should optimize the report, and then send it over"),
            NoCommaBeforeAnd = true,
            BritishSpelling = true,
        };

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        injector.Injected.ShouldBe(["We should optimise the report and then send it over"]);
    }

    [Fact]
    public async Task A_cleaner_that_never_answers_is_cut_off_by_the_budget()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var capture = FakeAudioCapture.Tone(0.6);
        var started = System.Diagnostics.Stopwatch.StartNew();
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello there, how are you"), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = new NeverCleaner(),
        };
        DictationResult? completed = null;
        engine.Completed += (_, r) => completed = r;

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        started.Elapsed.ShouldBeLessThan(DictationEngine.CleanupBudget + TimeSpan.FromSeconds(3));
        injector.Injected.ShouldBe(["hello there, how are you"]);
        completed.ShouldNotBeNull().Cleanup.ShouldBe(CleanupPath.LocalFallback);
    }

    [Fact]
    public async Task Two_dictations_into_the_same_field_are_joined()
    {
        var hotkey = new FakeHotkeySource { UserKeyPresses = 3 };
        var injector = new RecordingTextInjector { FocusTarget = "chat/box" };
        var capture = FakeAudioCapture.Tone(0.6);
        var transcriber = new FakeTranscriber("Sounds good.", "Next point.");
        await using var engine = new DictationEngine(capture, hotkey, transcriber, injector, () => [])
        {
            FullStops = TrailingFullStop.Never,
        };
        DictationResult? completed = null;
        engine.Completed += (_, r) => completed = r;

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);
        injector.CaretText = "Sounds good";
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Deliveries == 2);
        hotkey.Release();
        await Wait.UntilAsync(() => injector.Injected.Count == 2 && engine.State == DictationState.Idle);

        injector.Injected.ShouldBe(["Sounds good", ". Next point"]);
        completed.ShouldNotBeNull().Text.ShouldBe("Next point", "the history keeps the dictation itself, not the join");
    }

    [Fact]
    public async Task A_chat_box_cleared_after_sending_does_not_get_a_leading_full_stop()
    {
        var hotkey = new FakeHotkeySource { UserKeyPresses = 3 };
        var injector = new RecordingTextInjector { FocusTarget = "chat/box" };
        var capture = FakeAudioCapture.Tone(0.6);
        var transcriber = new FakeTranscriber("Previous thought.", "Okay, finish up.");
        await using var engine = new DictationEngine(capture, hotkey, transcriber, injector, () => [])
        {
            FullStops = TrailingFullStop.Never,
        };

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        // Clicking Send clears the same control without producing a keyboard event. The
        // focus identity and keypress count therefore still match, but the caret reader
        // proves the previous dictation is no longer in the field.
        injector.CaretText = string.Empty;
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Deliveries == 2);
        hotkey.Release();
        await Wait.UntilAsync(() => injector.Injected.Count == 2 && engine.State == DictationState.Idle);

        injector.Injected.ShouldBe(["Previous thought", "Okay, finish up"]);
    }

    [Fact]
    public async Task Typing_in_between_means_no_join()
    {
        var hotkey = new FakeHotkeySource { UserKeyPresses = 3 };
        var injector = new RecordingTextInjector { FocusTarget = "chat/box" };
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("Sounds good.", "Next point."), injector, () => [])
        {
            FullStops = TrailingFullStop.Never,
        };

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);
        hotkey.UserKeyPresses = 4;
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Deliveries == 2);
        hotkey.Release();
        await Wait.UntilAsync(() => injector.Injected.Count == 2 && engine.State == DictationState.Idle);

        injector.Injected.ShouldBe(["Sounds good", "Next point"]);
    }

    private sealed class RecordingCleaner(string? reply) : ITranscriptCleaner
    {
        public List<string> Texts { get; } = [];
        public string Name => "recording";
        public Task<string?> CleanAsync(string text, CancellationToken cancellationToken)
        {
            Texts.Add(text);
            return Task.FromResult(reply);
        }
    }

    private sealed class NeverCleaner : ITranscriptCleaner
    {
        public string Name => "never";
        public async Task<string?> CleanAsync(string text, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private sealed class OrderInjector(List<string> order) : ITextInjector
    {
        public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
        {
            order.Add("inject");
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task A_failed_cleanup_types_the_local_text_and_reports_it()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var faults = new List<string>();

        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello there, how are you"), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = new StubCleaner(null),
        };
        engine.Faulted += (_, m) => faults.Add(m);

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        injector.Injected.ShouldBe(["hello there, how are you"]);
        faults.ShouldHaveSingleItem().ShouldContain("local transcript");
    }

    [Fact]
    public async Task The_cleaner_is_not_called_when_the_tier_is_off()
    {
        var hotkey = new FakeHotkeySource();
        var cleaner = new StubCleaner("should not appear");
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("raw"), new RecordingTextInjector(), () => [])
        {
            AiCleanup = false,
            Cleaner = cleaner,
        };

        hotkey.Press();
        await DrainAndReleaseAsync(hotkey, engine, capture);

        cleaner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Tap_to_toggle_starts_on_one_press_and_stops_on_the_next()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("toggled"), injector, () => [])
        {
            TapToToggle = true,
        };

        hotkey.Press();
        hotkey.Release();   // ignored in toggle mode
        await Wait.UntilAsync(() => capture.Delivered);
        engine.State.ShouldBe(DictationState.Recording, "release must not stop a toggled recording");

        hotkey.Press();     // second tap stops
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        injector.Injected.ShouldBe(["toggled"]);
    }

    private sealed class StubCleaner(string? reply) : ITranscriptCleaner
    {
        public int Calls { get; private set; }
        public string Name => "stub";
        public Task<string?> CleanAsync(string text, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(reply);
        }
    }
}

/// <summary>Recording a chord goes through the hook, not a window.</summary>
public sealed class ChordCaptureTests
{
    [Fact]
    public async Task Capture_reports_the_chord_and_the_engine_applies_it()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("x"), new RecordingTextInjector(), () => []);

        (int Key, int Mods)? got = null;
        engine.Captured += (_, chord) => got = chord;

        engine.BeginCapture();
        hotkey.IsCapturing.ShouldBeTrue();
        hotkey.Capture(0x20, (int)(Murmur.Abstractions.HotkeyModifiers.Control | Murmur.Abstractions.HotkeyModifiers.Alt));

        got.ShouldNotBeNull();
        got.Value.Key.ShouldBe(0x20);
        got.Value.Mods.ShouldBe(5);

        engine.HotkeyVirtualKey = got.Value.Key;
        engine.HotkeyModifiers = got.Value.Mods;
        hotkey.VirtualKey.ShouldBe(0x20);
        hotkey.Modifiers.ShouldBe(5);
    }
}

/// <summary>The engine keeps the raw transcript when the cleaner rewrites it.</summary>
public sealed class CleanupGuardInEngineTests
{
    [Fact]
    public async Task A_summarising_cleaner_is_ignored_without_a_fault()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var faults = new List<string>();
        const string raw = "I think this is fine and we should go ahead with the plan as discussed";

        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber(raw), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = new Summariser(),
        };
        engine.Faulted += (_, m) => faults.Add(m);
        DictationResult? completed = null;
        engine.Completed += (_, r) => completed = r;

        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        injector.Injected.ShouldBe([raw]);
        completed.ShouldNotBeNull().CleanedBy.ShouldBeNull();
        faults.ShouldBeEmpty();
    }

    private sealed class Summariser : ITranscriptCleaner
    {
        public string Name => "summariser";
        public Task<string?> CleanAsync(string text, CancellationToken cancellationToken) => Task.FromResult<string?>("Go ahead.");
    }
}

/// <summary>Automatic mode, cancel, preview and the raw text on results.</summary>
public sealed class ActivationAndPreviewTests
{
    private static async Task WaitForAsync(Func<bool> condition)
    {
        await Wait.UntilAsync(() => condition());
    }

    [Fact]
    public async Task Automatic_mode_treats_a_quick_tap_as_a_toggle_and_a_hold_as_push_to_talk()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var clock = new FakeClock();

        var capture = FakeAudioCapture.Tone(2);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello"), injector, () => [], clock)
        {
            Mode = ActivationMode.Automatic,
        };

        // Tap: press and release within the threshold keeps recording.
        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Recording);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        hotkey.Release();
        await Task.Delay(50);
        engine.State.ShouldBe(DictationState.Recording);

        // The next press stops it.
        clock.Advance(TimeSpan.FromSeconds(1));
        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Idle);
        injector.Injected.ShouldBe(["hello"]);

        // Hold: press, wait past the threshold, release ends it. This is the fake's second
        // run, so wait for its second delivery — the first already made Delivered true.
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Deliveries == 2);
        clock.Advance(TimeSpan.FromSeconds(1));
        hotkey.Release();
        await WaitForAsync(() => engine.State == DictationState.Idle);
        injector.Injected.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Escape_discards_the_recording_and_types_nothing()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();

        var capture = FakeAudioCapture.Tone(2);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello"), injector, () => [])
        {
            Mode = ActivationMode.Tap,
        };

        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Recording);
        hotkey.PressCancel();
        await WaitForAsync(() => engine.State == DictationState.Idle);

        injector.Injected.ShouldBeEmpty();
        engine.Preview.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task The_result_carries_the_raw_transcript_and_the_rules_apply_locally()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        DictationResult? completed = null;

        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("Um, send it Friday. Scratch that. Send it Thursday, new line, thanks."), injector, () => []);
        engine.Completed += (_, r) => completed = r;

        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await WaitForAsync(() => engine.State == DictationState.Idle);

        injector.Injected.ShouldBe(["Send it Thursday\nThanks"]);
        completed.ShouldNotBeNull().RawText.ShouldBe("Um, send it Friday. Scratch that. Send it Thursday, new line, thanks.");
    }

    [Fact]
    public async Task Never_means_no_trailing_full_stop_even_on_prose()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();

        var capture = FakeAudioCapture.Tone(0.6);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("First thing. Second thing."), injector, () => [])
        {
            FullStops = TrailingFullStop.Never,
        };

        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await WaitForAsync(() => engine.State == DictationState.Idle);

        injector.Injected.ShouldBe(["First thing. Second thing"]);
    }
}
