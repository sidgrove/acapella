using System.Runtime.CompilerServices;
using System.Text.Json;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Speech;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// Streaming cloud transcription: its words are typed when it answers, and the local
/// model's are typed, unchanged from before, whenever it does not.
/// </summary>
public sealed class CloudTranscriptionTests
{
    private sealed class EchoCleaner : ITranscriptCleaner
    {
        public List<string> Seen { get; } = [];
        public List<(string Cloud, string Local)> Pairs { get; } = [];
        public string Name => "echo";

        public Task<string?> CleanTwoReadingsAsync(string cloud, string local, CancellationToken cancellationToken)
        {
            lock (Pairs) Pairs.Add((cloud, local));
            return Task.FromResult<string?>(cloud);
        }

        public Task<string?> CleanAsync(string text, CancellationToken cancellationToken)
        {
            lock (Seen) Seen.Add(text);
            return Task.FromResult<string?>(text);
        }
    }

    private static async Task<(DictationResult? Result, RecordingTextInjector Injector)> DictateAsync(
        IStreamingTranscriber cloud, IAudioCapture? capture = null, ITranscriptCleaner? cleaner = null,
        Func<IReadOnlyList<DictionaryEntry>>? dictionary = null, TimeSpan? budget = null, IAudioDucker? ducker = null)
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var audio = capture ?? FakeAudioCapture.Tone(0.8);
        DictationResult? completed = null;

        await using var engine = new DictationEngine(audio, hotkey, new FakeTranscriber("the local words"), injector, dictionary ?? (() => []))
        {
            CloudTranscriber = cloud,
            AiCleanup = cleaner is not null,
            Cleaner = cleaner,
            Ducker = ducker,
        };
        if (budget is { } within) engine.CloudBudget = within;
        engine.Completed += (_, r) => completed = r;

        hotkey.Press();
        await Wait.UntilAsync(() => audio is FakeAudioCapture f ? f.Delivered : audio is PreRollCapture p && p.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle && completed is not null);
        return (completed, injector);
    }

    [Fact]
    public async Task The_clouds_words_are_typed_and_the_local_ones_kept_for_comparison()
    {
        var cloud = new FakeStreamingTranscriber("the cloud words");
        var (result, injector) = await DictateAsync(cloud);

        injector.Injected.ShouldBe(["the cloud words"]);
        result.ShouldNotBeNull();
        result.RawText.ShouldBe("the cloud words");
        result.TranscribedBy.ShouldBe("fake-cloud");
        result.LocalRawText.ShouldBe("the local words");
    }

    [Fact]
    public async Task The_cleaner_is_given_the_clouds_words()
    {
        var cleaner = new EchoCleaner();
        await DictateAsync(new FakeStreamingTranscriber("run Impeccable over the serif headings"), cleaner: cleaner);

        cleaner.Pairs.ShouldHaveSingleItem().Cloud.ShouldBe("run Impeccable over the serif headings");
    }

    [Fact]
    public async Task Differing_readings_both_go_to_the_cleaner()
    {
        var cleaner = new EchoCleaner();
        await DictateAsync(new FakeStreamingTranscriber("see if the serif fits"), cleaner: cleaner);

        cleaner.Pairs.ShouldBe([("see if the serif fits", "the local words")]);
        cleaner.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task Readings_with_the_same_words_go_to_the_cleaner_once()
    {
        var cleaner = new EchoCleaner();
        await DictateAsync(new FakeStreamingTranscriber("The local words."), cleaner: cleaner);

        cleaner.Pairs.ShouldBeEmpty();
        cleaner.Seen.ShouldBe(["The local words."]);
    }

    [Fact]
    public async Task A_failed_cloud_falls_back_to_the_local_words()
    {
        var (result, injector) = await DictateAsync(new FakeStreamingTranscriber(null));

        injector.Injected.ShouldBe(["the local words"]);
        result.ShouldNotBeNull().TranscribedBy.ShouldBeNull();
        result.LocalRawText.ShouldBeNull();
    }

    [Fact]
    public async Task An_empty_cloud_answer_is_not_taken_as_silence()
    {
        var (_, injector) = await DictateAsync(new FakeStreamingTranscriber("   "));
        injector.Injected.ShouldBe(["the local words"]);
    }

    [Fact]
    public async Task A_late_cloud_is_not_waited_for_past_the_budget()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (_, injector) = await DictateAsync(new FakeStreamingTranscriber("too late", TimeSpan.FromSeconds(30)), budget: TimeSpan.FromMilliseconds(200));

        injector.Injected.ShouldBe(["the local words"]);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Every_sample_is_streamed_the_dictation_is_ended_and_the_socket_released()
    {
        var capture = FakeAudioCapture.Tone(0.8);
        var cloud = new FakeStreamingTranscriber("fine");
        await DictateAsync(cloud, capture);

        var session = cloud.Started.ShouldHaveSingleItem();
        session.Audio.Length.ShouldBe(capture.Samples.Length);
        session.Finished.ShouldBeTrue();
        await Wait.UntilAsync(() => session.Disposed);
    }

    [Fact]
    public async Task The_dictionary_goes_to_the_cloud_as_its_word_list()
    {
        var cloud = new FakeStreamingTranscriber("fine");
        await DictateAsync(cloud, dictionary: () => DictionaryFile.Parse("Supabase\nget pole -> git pull"));

        cloud.Started.ShouldHaveSingleItem().KeyTerms.ShouldBe(["Supabase", "git pull"], ignoreOrder: true);
    }

    [Fact]
    public async Task Pre_roll_under_loud_playback_never_reaches_the_cloud()
    {
        var capture = new PreRollCapture();
        var cloud = new FakeStreamingTranscriber("fine");
        await DictateAsync(cloud, capture, ducker: new FakeAudioDucker { Peak = 0.6f });

        cloud.Started.ShouldHaveSingleItem().Audio.Length
            .ShouldBe((int)(PreRollCapture.TotalSeconds * AudioChunk.SampleRate) - (int)(PreRollCapture.PreRollSeconds * AudioChunk.SampleRate));
    }

    [Fact]
    public void The_setup_turns_off_pause_detection_asks_for_verbatim_and_carries_the_word_list()
    {
        using var json = JsonDocument.Parse(GeminiLiveTranscriber.Setup("gemini-3.5-transcribe-live", "en-GB", ["Supabase", "supabase", " ", "shadcn"]));
        var setup = json.RootElement.GetProperty("setup");

        setup.GetProperty("model").GetString().ShouldBe("models/gemini-3.5-transcribe-live");
        setup.GetProperty("realtimeInputConfig").GetProperty("automaticActivityDetection").GetProperty("disabled").GetBoolean().ShouldBeTrue();
        var transcription = setup.GetProperty("inputAudioTranscription");
        transcription.GetProperty("mode").GetString().ShouldBe("VERBATIM");
        transcription.GetProperty("languageCodes")[0].GetString().ShouldBe("en-GB");
        transcription.GetProperty("customVocabulary").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Supabase", "shadcn"]);
    }

    [Fact]
    public void Scribe_is_committed_by_hand_and_gets_only_the_terms_it_accepts()
    {
        var uri = ElevenLabsTranscriber.Endpoint("scribe_v2_realtime", "en", ["Supabase", "supabase", "a phrase far longer than twenty characters", "git pull"]).AbsoluteUri;

        uri.ShouldStartWith("wss://api.elevenlabs.io/v1/speech-to-text/realtime?model_id=scribe_v2_realtime");
        uri.ShouldContain("commit_strategy=manual");
        uri.ShouldContain("audio_format=pcm_16000");
        uri.ShouldContain("keyterms=Supabase");
        uri.ShouldContain("keyterms=git%20pull");
        uri.ShouldNotContain("keyterms=supabase", Case.Sensitive);
        uri.ShouldNotContain("twenty");
    }

    [Fact]
    public void No_key_means_no_cloud_transcription()
    {
        new GeminiLiveTranscriber(() => null).Start(["x"]).ShouldBeNull();
    }

    /// <summary>A tone capture that reports its first 0.4 s as pre-roll.</summary>
    private sealed class PreRollCapture : IAudioCapture
    {
        public const double PreRollSeconds = 0.4;
        public const double TotalSeconds = 1.2;
        private readonly FakeAudioCapture _inner = FakeAudioCapture.Tone(TotalSeconds);
        public bool IsCapturing => _inner.IsCapturing;
        public bool LooksLikeBlockedMicrophone => false;
        public bool Delivered => _inner.Delivered;
        public TimeSpan PreRollDelivered => TimeSpan.FromSeconds(PreRollSeconds);

        public async IAsyncEnumerable<AudioChunk> CaptureAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var chunk in _inner.CaptureAsync(cancellationToken).ConfigureAwait(false)) yield return chunk;
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}

/// <summary>The WAV encoding and the recordings kept on disk.</summary>
public sealed class RecordingArchiveTests
{
    [Fact]
    public void A_wav_round_trips_within_one_sixteen_bit_step()
    {
        float[] samples = [0f, 0.5f, -0.5f, 1f, -1f, 2f];
        var decoded = WaveFile.Decode(WaveFile.Encode(samples), out var rate);

        rate.ShouldBe(AudioChunk.SampleRate);
        decoded.Length.ShouldBe(samples.Length);
        for (var i = 0; i < samples.Length; i++) decoded[i].ShouldBe(Math.Clamp(samples[i], -1f, 1f), 1f / short.MaxValue);
    }

    [Fact]
    public void A_recording_is_saved_under_its_release_time_and_the_oldest_go_past_the_limit()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"acapella-archive-{Guid.NewGuid():N}");
        try
        {
            var archive = new RecordingArchive(folder) { MaxBytes = 2 * (44 + 3200) };
            var at = new DateTimeOffset(2026, 9, 24, 14, 0, 0, TimeSpan.FromHours(1));
            for (var i = 0; i < 3; i++) archive.Save(at.AddSeconds(i), new float[1600]);

            File.Exists(archive.PathFor(at)).ShouldBeFalse("the oldest is dropped once the folder is over its limit");
            File.Exists(archive.PathFor(at.AddSeconds(1))).ShouldBeTrue();
            File.Exists(archive.PathFor(at.AddSeconds(2))).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Only_a_corrected_recording_is_kept_and_one_never_settled_goes_after_the_hour()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"acapella-archive-{Guid.NewGuid():N}");
        try
        {
            var archive = new RecordingArchive(folder);
            var at = new DateTimeOffset(2026, 9, 25, 14, 0, 0, TimeSpan.FromHours(1));
            archive.Save(at, new float[1600]);
            archive.Save(at.AddSeconds(1), new float[1600]);
            archive.Save(at.AddSeconds(2), new float[1600]);

            archive.Keep(at);
            archive.Discard(at.AddSeconds(1));
            File.SetLastWriteTime(archive.PathFor(at.AddSeconds(2)), DateTime.Now.AddHours(-2));
            archive.Save(at.AddSeconds(3), new float[1600]);

            archive.Find(at).ShouldNotBeNull().ShouldStartWith(archive.KeptFolder);
            archive.Find(at.AddSeconds(1)).ShouldBeNull();
            archive.Find(at.AddSeconds(2)).ShouldBeNull("never settled, so gone once it is over an hour old");
            archive.Find(at.AddSeconds(3)).ShouldNotBeNull("still waiting on its edit check");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
