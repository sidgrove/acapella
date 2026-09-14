using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The microphone stays warm between dictations and the moments before the key press are kept.</summary>
public sealed class WarmAudioCaptureTests
{
    /// <summary>A device that streams whatever the test pushes, until cancelled.</summary>
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

    private static async Task<List<float>> TakeAsync(WarmAudioCapture capture, int chunks, CancellationTokenSource stop)
    {
        var seen = new List<float>();
        try
        {
            await foreach (var chunk in capture.CaptureAsync(stop.Token))
            {
                seen.Add(chunk.Samples.Span[0]);
                if (seen.Count == chunks) stop.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            // The key was released; the engine swallows this the same way.
        }
        return seen;
    }

    [Fact]
    public async Task Second_recording_starts_with_the_audio_from_just_before_the_key_press()
    {
        var device = new PushCapture();
        await using var warm = new WarmAudioCapture(device, preRoll: TimeSpan.FromSeconds(1), idleTimeout: TimeSpan.FromMinutes(1));

        device.Push(1f);
        device.Push(2f);
        var first = await TakeAsync(warm, 2, new CancellationTokenSource());
        first.ShouldBe([1f, 2f]);
        warm.IsWarm.ShouldBeTrue("the device stays open after a recording");
        device.IsCapturing.ShouldBeTrue();

        // Spoken between recordings, i.e. as the key is being pressed.
        device.Push(3f);
        device.Push(4f);
        await Task.Delay(50);

        device.Push(5f);
        var second = await TakeAsync(warm, 3, new CancellationTokenSource());
        second.ShouldBe([3f, 4f, 5f], "pre-roll first, then live audio");
        device.Opens.ShouldBe(1, "the device was never reopened");
    }

    [Fact]
    public async Task Warming_up_opens_the_device_before_the_first_recording()
    {
        var device = new PushCapture();
        await using var warm = new WarmAudioCapture(device, preRoll: TimeSpan.FromSeconds(1));

        warm.WarmUp();
        (await Wait.UntilAsync(() => device.IsCapturing)).ShouldBeTrue("the device opens at start-up, not on the key press");
        warm.IsCapturing.ShouldBeFalse("nothing is recording yet");

        // Said as the fingers land on the keys.
        device.Push(7f);
        await Task.Delay(50);
        device.Push(8f);

        var first = await TakeAsync(warm, 2, new CancellationTokenSource());
        first.ShouldBe([7f, 8f], "the very first recording already has pre-roll");
        device.Opens.ShouldBe(1);

        warm.Release();
        (await Wait.UntilAsync(() => !device.IsCapturing)).ShouldBeTrue("switching dictation off closes the device");
        warm.IsWarm.ShouldBeFalse();
    }

    [Fact]
    public async Task Pre_roll_is_bounded_to_the_newest_audio()
    {
        var device = new PushCapture();
        await using var warm = new WarmAudioCapture(device, preRoll: TimeSpan.FromMilliseconds(20), idleTimeout: TimeSpan.FromMinutes(1));

        device.Push(1f);
        (await TakeAsync(warm, 1, new CancellationTokenSource())).ShouldBe([1f]);

        for (var i = 10; i < 20; i++) device.Push(i);   // 100 ms of audio into a 20 ms ring
        await Task.Delay(50);
        device.Push(99f);

        var seen = await TakeAsync(warm, 3, new CancellationTokenSource());
        seen[^1].ShouldBe(99f);
        seen.Count(v => v is >= 10 and < 20).ShouldBeLessThanOrEqualTo(2);
        seen.ShouldContain(19f, "the newest pre-roll survives");
        seen.ShouldNotContain(10f, "the oldest is dropped");
    }

    [Fact]
    public async Task Microphone_is_released_after_the_idle_timeout()
    {
        var device = new PushCapture();
        await using var warm = new WarmAudioCapture(device, preRoll: TimeSpan.FromMilliseconds(400), idleTimeout: TimeSpan.FromMilliseconds(50));

        device.Push(1f);
        (await TakeAsync(warm, 1, new CancellationTokenSource())).ShouldBe([1f]);

        (await Wait.UntilAsync(() => !device.IsCapturing)).ShouldBeTrue("the device closes once idle");
        warm.IsWarm.ShouldBeFalse();

        device.Push(2f);
        (await TakeAsync(warm, 1, new CancellationTokenSource())).ShouldBe([2f]);
        device.Opens.ShouldBe(2, "a later recording reopens it");
    }

    [Fact]
    public async Task Device_failure_mid_recording_reaches_the_recording()
    {
        var device = FakeAudioCapture.Tone(0.1);   // ends on its own after 100 ms of audio
        await using var warm = new WarmAudioCapture(device);

        var chunks = 0;
        await foreach (var _ in warm.CaptureAsync(CancellationToken.None)) chunks++;
        chunks.ShouldBeGreaterThan(0, "the recording sees the audio, then ends when the device does");
        warm.IsWarm.ShouldBeFalse();
    }
}
