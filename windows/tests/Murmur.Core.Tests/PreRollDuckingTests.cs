using System.Runtime.CompilerServices;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// Pre-roll recorded while a video was still playing at full volume is the video's words,
/// not the user's, and must not be transcribed.
/// </summary>
public sealed class PreRollDuckingTests
{
    private const double PreRollSeconds = 0.4;
    private const double TotalSeconds = 1.2;

    /// <summary>A tone capture that reports part of its audio as pre-roll.</summary>
    private sealed class PreRollCapture : IAudioCapture
    {
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

    private static async Task<int> FinalDecodeLengthAsync(float peak)
    {
        var hotkey = new FakeHotkeySource();
        var capture = new PreRollCapture();
        var transcriber = new FakeTranscriber("hello");
        await transcriber.LoadAsync(CancellationToken.None);
        var ducker = new FakeAudioDucker { Peak = peak };

        await using var engine = new DictationEngine(capture, hotkey, transcriber, new RecordingTextInjector(), () => [])
        {
            Ducker = ducker,
        };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        ducker.Calls.ShouldContain("duck");
        return transcriber.SegmentLengths[^1];
    }

    private static int All => (int)(TotalSeconds * AudioChunk.SampleRate);
    private static int WithoutPreRoll => All - (int)(PreRollSeconds * AudioChunk.SampleRate);

    [Fact]
    public async Task Pre_roll_is_dropped_when_other_audio_was_playing_loudly()
    {
        var decoded = await FinalDecodeLengthAsync(peak: 0.6f);
        decoded.ShouldBe(WithoutPreRoll, "the video's 400 ms must not reach the model");
    }

    [Fact]
    public async Task Pre_roll_is_kept_when_the_room_was_quiet()
    {
        var decoded = await FinalDecodeLengthAsync(peak: 0f);
        decoded.ShouldBe(All, "a first word spoken as the key lands is kept");
    }

    [Fact]
    public async Task Pre_roll_is_kept_over_a_resting_mixer()
    {
        // A studio machine keeps a session active at a few percent all day. That is not a
        // video; dropping the pre-roll for it loses the first word of every dictation.
        var decoded = await FinalDecodeLengthAsync(peak: 0.05f);
        decoded.ShouldBe(All);
    }
}
