using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>What the key-up waits for, and what it no longer does.</summary>
public sealed class KeyUpLatencyTests
{
    /// <summary>Holds the first decode, which is always the preview's, until told to let go.</summary>
    private sealed class HeldPreviewTranscriber : ITranscriber
    {
        private int _calls;

        public TaskCompletionSource PreviewStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePreview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsReady => true;

        public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public async ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                PreviewStarted.TrySetResult();
                await ReleasePreview.Task.ConfigureAwait(false);
                return "the preview";
            }
            return "the local words";
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_dictation_is_typed_while_the_previews_redecode_is_still_running()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var capture = FakeAudioCapture.Tone(1.5);
        var transcriber = new HeldPreviewTranscriber();
        var cloud = new FakeStreamingTranscriber("the cloud words");

        await using var engine = new DictationEngine(capture, hotkey, transcriber, injector, () => []) { CloudTranscriber = cloud };
        try
        {
            hotkey.Press();
            await transcriber.PreviewStarted.Task.WaitAsync(Wait.Timeout);
            hotkey.Release();

            // Before 25/09/2026 the key-up waited for this decode, whose text is only ever
            // shown on screen, and the cloud was not even asked to finish until it was over.
            (await Wait.UntilAsync(() => injector.Injected.Count > 0)).ShouldBeTrue("typed without waiting for the preview");
            injector.Injected.ShouldBe(["the cloud words"]);
            cloud.Started.ShouldHaveSingleItem().Finished.ShouldBeTrue();
        }
        finally
        {
            transcriber.ReleasePreview.TrySetResult();
        }
    }

    [Fact]
    public async Task With_the_cloud_on_a_frozen_piece_is_never_just_the_silence_before_the_first_word()
    {
        // Two seconds of nothing, then twelve of speech: the quietest point anywhere in the
        // search is that silence, and on 25/09/2026 a 0.3 s piece was frozen from it.
        var samples = new float[14 * AudioChunk.SampleRate];
        var noise = FakeAudioCapture.Noise(12).Samples.Span;
        noise.CopyTo(samples.AsSpan(2 * AudioChunk.SampleRate));
        var capture = new FakeAudioCapture(samples);
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("words");
        await transcriber.LoadAsync(CancellationToken.None);

        await using var engine = new DictationEngine(capture, hotkey, transcriber, new RecordingTextInjector(), () => [])
        {
            CloudTranscriber = new FakeStreamingTranscriber("the cloud words"),
        };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        (await Wait.UntilAsync(() => transcriber.SegmentLengths.Count >= 2)).ShouldBeTrue();
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        var window = (int)(DictationEngine.CloudPreviewWindow.TotalSeconds * AudioChunk.SampleRate);
        transcriber.SegmentLengths[0].ShouldBeGreaterThanOrEqualTo(window / 4);
    }
}
