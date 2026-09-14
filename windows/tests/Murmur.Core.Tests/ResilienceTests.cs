using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Speech;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// The findings of the 2026-09-12 review, each pinned so it cannot come back: a stale
/// blocked-microphone flag, a preview that decoded the whole buffer, a warm stream that
/// ignored a microphone change, stores shared between threads, a truncated AI reply typed
/// as if complete, and settings migrations that re-ran on every launch.
/// </summary>
public sealed class ResilienceTests
{
    private static async Task DictateAsync(FakeHotkeySource hotkey, DictationEngine engine, FakeAudioCapture capture)
    {
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
    }

    [Fact]
    public async Task Audible_speech_is_transcribed_even_if_the_stream_once_looked_blocked()
    {
        // A headset muted for a call and unmuted since: the flag was set while it was muted
        // and the recording itself is perfectly audible. The words must be typed.
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(1.0);
        capture.LooksLikeBlockedMicrophone = true;
        var injector = new RecordingTextInjector();
        var faults = new List<string>();

        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello"), injector, () => []);
        engine.Faulted += (_, m) => faults.Add(m);

        await DictateAsync(hotkey, engine, capture);

        injector.Injected.ShouldBe(["hello"]);
        faults.ShouldBeEmpty();
    }

    [Fact]
    public async Task Silence_reports_that_nothing_was_heard_rather_than_vanishing()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Silence(1.0);
        var dropped = new List<string>();

        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("x"), new RecordingTextInjector(), () => []);
        engine.Dropped += (_, why) => dropped.Add(why);

        await DictateAsync(hotkey, engine, capture);

        dropped.ShouldHaveSingleItem().ShouldBe("Nothing heard");
    }

    [Fact]
    public async Task Preview_decodes_only_the_newest_window_and_keeps_earlier_text()
    {
        // Thirty seconds buffered against a 25-second window: the loop must freeze the
        // older part once and re-decode only the tail — never the whole buffer, which at
        // sixty seconds costs gigabytes and past four hundred throws.
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Noise(PreviewWindowSeconds + 5);
        // Keyed on where each segment starts, not on call order: a slow runner's preview
        // ticks before the fake has delivered everything, and then the calls come in a
        // different order from a fast machine's.
        var transcriber = FakeTranscriber.ByOffset(capture.Samples, atStart: "earlier", later: "later");
        await transcriber.LoadAsync(CancellationToken.None);

        await using var engine = new DictationEngine(capture, hotkey, transcriber, new RecordingTextInjector(), () => []);
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);

        (await Wait.UntilAsync(() => engine.Preview == "earlier later")).ShouldBeTrue($"preview was '{engine.Preview}'");

        var total = (int)((PreviewWindowSeconds + 5) * AudioChunk.SampleRate);
        transcriber.SegmentLengths.ShouldAllBe(length => length < total);
        transcriber.SegmentLengths[0].ShouldBeLessThan(PreviewWindowSeconds * AudioChunk.SampleRate);

        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
        engine.Preview.ShouldBe(string.Empty);
    }

    private static int PreviewWindowSeconds => (int)DictationEngine.PreviewWindow.TotalSeconds;

    [Fact]
    public async Task A_missed_key_up_in_hold_mode_ends_the_recording()
    {
        var hotkey = new ReleasableHotkey();
        var capture = FakeAudioCapture.Tone(1.0);
        var injector = new RecordingTextInjector();

        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("done"), injector, () => [])
        {
            Mode = ActivationMode.Hold,
        };

        hotkey.Held = true;
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);

        // The key comes up but the hook never sees the release (an elevated window took it).
        hotkey.Held = false;

        (await Wait.UntilAsync(() => engine.State == DictationState.Idle)).ShouldBeTrue();
        injector.Injected.ShouldBe(["done"]);
    }

    [Fact]
    public async Task A_new_recording_can_start_while_the_previous_one_is_still_transcribing()
    {
        // Two sentences back to back, the second pressed while the first is still in the
        // model. Both must be typed, in the order they were spoken.
        var hotkey = new FakeHotkeySource();
        var device = new PushCapture();
        var transcriber = new SlowTranscriber(TimeSpan.FromMilliseconds(400), "first", "second");
        var injector = new RecordingTextInjector();

        await using var engine = new DictationEngine(device, hotkey, transcriber, injector, () => []);

        hotkey.Press();
        for (var i = 0; i < 100; i++) device.Push(0.5f);   // 1 s of signal
        await Wait.UntilAsync(() => engine.IsCaptureReady && engine.Level > 0);
        await Task.Delay(50);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Transcribing);

        hotkey.Press();
        (await Wait.UntilAsync(() => engine.State == DictationState.Recording)).ShouldBeTrue("a press during transcription starts a new recording");
        for (var i = 0; i < 100; i++) device.Push(0.5f);
        await Wait.UntilAsync(() => engine.IsCaptureReady);
        await Task.Delay(50);
        hotkey.Release();

        (await Wait.UntilAsync(() => engine.State == DictationState.Idle)).ShouldBeTrue();
        injector.Injected.ShouldBe(["first", "second"]);
        device.Opens.ShouldBe(2);
    }

    [Fact]
    public async Task Disposal_during_transcription_waits_for_it()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(1.0);
        var transcriber = new SlowTranscriber(TimeSpan.FromMilliseconds(300));
        var injector = new RecordingTextInjector();

        var engine = new DictationEngine(capture, hotkey, transcriber, injector, () => []);
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Transcribing);

        await engine.DisposeAsync();

        transcriber.DisposedWhileDecoding.ShouldBeFalse("the model must not be torn down under a running decode");
        injector.Injected.ShouldBe(["slow"]);
    }

    [Fact]
    public async Task Choosing_a_different_microphone_reopens_the_device_for_the_next_recording()
    {
        var device = new PushCapture();
        await using var warm = new WarmAudioCapture(device, preRoll: TimeSpan.FromMilliseconds(100), idleTimeout: TimeSpan.FromMinutes(1));

        device.Push(1f);
        (await TakeAsync(warm, 1)).ShouldBe([1f]);
        warm.IsWarm.ShouldBeTrue();
        device.Opens.ShouldBe(1);

        warm.ReopenDevice();
        (await Wait.UntilAsync(() => device.Opens == 2)).ShouldBeTrue("the device is closed and opened afresh at once, ready for the next key press");
        (await Wait.UntilAsync(() => device.IsCapturing)).ShouldBeTrue();

        device.Push(2f);
        (await TakeAsync(warm, 1)).ShouldBe([2f]);
        device.Opens.ShouldBe(2, "the recording used the reopened device, not a third one");
    }

    [Fact]
    public async Task A_microphone_change_during_a_recording_waits_for_it_to_end()
    {
        var device = new PushCapture();
        await using var warm = new WarmAudioCapture(device, preRoll: TimeSpan.FromMilliseconds(100), idleTimeout: TimeSpan.FromMinutes(1));

        using var stop = new CancellationTokenSource();
        var seen = new List<float>();
        var recording = Task.Run(async () =>
        {
            try
            {
                await foreach (var chunk in warm.CaptureAsync(stop.Token))
                {
                    seen.Add(chunk.Samples.Span[0]);
                    if (seen.Count == 2) stop.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        });

        device.Push(1f);
        await Wait.UntilAsync(() => seen.Count == 1);
        warm.ReopenDevice();
        device.IsCapturing.ShouldBeTrue("a recording in progress keeps its device");
        device.Push(2f);
        await recording;

        (await Wait.UntilAsync(() => !device.IsCapturing)).ShouldBeTrue("released once the recording ended");
        device.Push(3f);
        (await TakeAsync(warm, 1)).ShouldBe([3f]);
        device.Opens.ShouldBe(2);
    }

    [Fact]
    public async Task Transcript_records_are_a_snapshot_that_a_concurrent_add_cannot_disturb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"murmur-history-{Guid.NewGuid():N}.jsonl");
        try
        {
            var store = new TranscriptStore(path);
            store.Add(new TranscriptRecord { Text = "one" });
            var snapshot = store.Records;

            // An enumeration in progress on the UI thread while the engine appends.
            var adder = Task.Run(() => { for (var i = 0; i < 200; i++) store.Add(new TranscriptRecord { Text = $"n{i}" }); });
            foreach (var record in snapshot) record.Text.ShouldNotBeNull();
            await adder;

            snapshot.Count.ShouldBe(1);
            store.Records.Count.ShouldBe(201);
            File.Exists(path + ".tmp").ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Dictionary_entries_are_a_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"murmur-dict-{Guid.NewGuid():N}.txt");
        try
        {
            var file = new DictionaryFile(path);
            file.Add(Murmur.Dictionary.DictionaryEntry.Term("Anthropic"));
            var snapshot = file.Entries;
            file.Add(Murmur.Dictionary.DictionaryEntry.Term("Vercel"));
            file.Remove(snapshot[0].Id);

            snapshot.Count.ShouldBe(1);
            file.Entries.Select(e => e.Write).ShouldBe(["Vercel"]);
            File.Exists(path + ".tmp").ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Legacy_settings_are_migrated_once_and_a_later_choice_sticks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"murmur-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"TapToToggle": true, "DropSingleSentenceFullStop": false}""");

            var first = new AppSettings(path);
            first.Data.Mode.ShouldBe(ActivationMode.Tap);
            first.Data.FullStops.ShouldBe(TrailingFullStop.Keep);

            // The user changes their mind in Settings.
            first.Update(first.Data with { Mode = ActivationMode.Automatic, FullStops = TrailingFullStop.DropAfterSingleSentence });

            var second = new AppSettings(path);
            second.Data.Mode.ShouldBe(ActivationMode.Automatic, "the old tap switch must not re-apply on the next launch");
            second.Data.FullStops.ShouldBe(TrailingFullStop.DropAfterSingleSentence);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void An_unreadable_settings_file_is_set_aside_not_overwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"murmur-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ this is not json");
            var settings = new AppSettings(path);
            settings.Data.PushToTalkKey.ShouldBe(0xA3);
            File.Exists(path + ".corrupt").ShouldBeTrue("the broken file is kept for inspection");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".corrupt")) File.Delete(path + ".corrupt");
        }
    }

    [Theory]
    [InlineData("STOP", "tidy text")]
    [InlineData("MAX_TOKENS", null)]
    [InlineData("SAFETY", null)]
    public async Task A_reply_that_did_not_finish_cleanly_is_never_typed(string finishReason, string? expected)
    {
        var body = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = "tidy text" } } }, finishReason },
            },
        });
        using var cleaner = new GeminiCleaner(() => "key", handler: new CannedHandler(body));

        var cleaned = await cleaner.CleanAsync("raw text", CancellationToken.None);

        cleaned.ShouldBe(expected);
        if (expected is null) cleaner.LastError.ShouldNotBeNull().ShouldContain(finishReason);
    }

    [Fact]
    public async Task The_request_no_longer_caps_the_reply_length()
    {
        var handler = new CannedHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}""");
        using var cleaner = new GeminiCleaner(() => "key", handler: handler);
        await cleaner.CleanAsync("raw", CancellationToken.None);
        handler.LastRequestBody.ShouldNotBeNull().ShouldNotContain("maxOutputTokens");
    }

    // ---- doubles ----

    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    /// <summary>A hotkey whose physical state can be set independently of the events it raises.</summary>
    private sealed class ReleasableHotkey : IHotkeySource
    {
        public event EventHandler? Pressed;
        public event EventHandler? Released { add { } remove { } }
        public event EventHandler? CancelPressed { add { } remove { } }
        public event EventHandler<(int VirtualKey, int Modifiers)>? Captured { add { } remove { } }
        public int VirtualKey { get; set; } = 0xA3;
        public int Modifiers { get; set; }
        public bool Held { get; set; }
        public bool IsTriggerHeld => Held;
        public void BeginCapture() { }
        public void CancelCapture() { }
        public bool Start() => true;
        public void StopListening() { }
        public void Press() => Pressed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }

    private sealed class SlowTranscriber(TimeSpan delay, params string[] replies) : ITranscriber
    {
        private readonly Queue<string> _replies = new(replies.Length == 0 ? ["slow"] : replies);
        private int _decoding;
        public bool DisposedWhileDecoding { get; private set; }
        public bool IsReady => true;
        public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public async ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _decoding);
            try
            {
                await Task.Delay(delay, cancellationToken);
                lock (_replies) return _replies.Count > 1 ? _replies.Dequeue() : _replies.Peek();
            }
            finally
            {
                Interlocked.Decrement(ref _decoding);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref _decoding) > 0) DisposedWhileDecoding = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PushCapture : IAudioCapture
    {
        private readonly Channel<float[]> _feed = Channel.CreateUnbounded<float[]>();
        public int Opens { get; private set; }
        public bool IsCapturing { get; private set; }
        public bool LooksLikeBlockedMicrophone => false;

        public void Push(float value, int samples = 160)
        {
            var chunk = new float[samples];
            Array.Fill(chunk, value);
            _feed.Writer.TryWrite(chunk);
        }

        public async IAsyncEnumerable<AudioChunk> CaptureAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Opens++;
            IsCapturing = true;
            try
            {
                await foreach (var chunk in _feed.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    yield return new AudioChunk(chunk);
            }
            finally { IsCapturing = false; }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<List<float>> TakeAsync(WarmAudioCapture capture, int chunks)
    {
        using var stop = new CancellationTokenSource();
        var seen = new List<float>();
        try
        {
            await foreach (var chunk in capture.CaptureAsync(stop.Token))
            {
                seen.Add(chunk.Samples.Span[0]);
                if (seen.Count == chunks) stop.Cancel();
            }
        }
        catch (OperationCanceledException) { }
        return seen;
    }
}
