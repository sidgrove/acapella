using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// A long dictation is transcribed and cleaned while it is still being spoken, so the
/// wait after the key-up covers only the last window of speech.
/// </summary>
public sealed class IncrementalCleanupTests
{
    private static int WindowSeconds => (int)DictationEngine.PreviewWindow.TotalSeconds;

    /// <summary>
    /// The frozen piece starts at the beginning of the buffer and reads one way; the live
    /// tail and the final decode start after the cut and read the other. Keyed on offset
    /// rather than call order so a slow runner, whose preview ticks mid-delivery, sees the
    /// same story.
    /// </summary>
    private static FakeTranscriber Piece_then_tail(FakeAudioCapture capture) =>
        FakeTranscriber.ByOffset(capture.Samples, atStart: "the quick brown fox jumped", later: "over the lazy sleeping dog");

    /// <summary>Upper-cases what it is given and remembers the context it was shown.</summary>
    private sealed class ContextCleaner : ITranscriptCleaner
    {
        public List<(string Text, string? Context)> Calls { get; } = [];
        public string Name => "context";

        public Task<string?> CleanAsync(string text, CancellationToken cancellationToken) => CleanAsync(text, null, cancellationToken);

        public Task<string?> CleanAsync(string text, string? precedingCleaned, CancellationToken cancellationToken)
        {
            Calls.Add((text, precedingCleaned));
            return Task.FromResult<string?>(text.ToUpperInvariant());
        }
    }

    [Fact]
    public async Task Final_pass_decodes_only_the_audio_after_the_frozen_preview()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Noise(WindowSeconds + 5);
        var transcriber = Piece_then_tail(capture);
        await transcriber.LoadAsync(CancellationToken.None);
        var injector = new RecordingTextInjector();

        await using var engine = new DictationEngine(capture, hotkey, transcriber, injector, () => []);
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        (await Wait.UntilAsync(() => engine.Preview.StartsWith("the quick brown fox jumped", StringComparison.Ordinal))).ShouldBeTrue();

        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        injector.Injected.ShouldHaveSingleItem().ToLowerInvariant().ShouldBe("the quick brown fox jumped over the lazy sleeping dog");
        transcriber.SegmentLengths[^1].ShouldBeLessThan(WindowSeconds * AudioChunk.SampleRate, "the final pass must not re-decode the frozen part");
    }

    [Fact]
    public async Task Frozen_pieces_are_cleaned_during_the_recording_and_only_the_tail_afterwards()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Noise(WindowSeconds + 5);
        var transcriber = Piece_then_tail(capture);
        await transcriber.LoadAsync(CancellationToken.None);
        var injector = new RecordingTextInjector();
        var cleaner = new ContextCleaner();

        await using var engine = new DictationEngine(capture, hotkey, transcriber, injector, () => [])
        {
            AiCleanup = true,
            Cleaner = cleaner,
        };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        (await Wait.UntilAsync(() => cleaner.Calls.Count == 1)).ShouldBeTrue("the frozen piece is cleaned while the key is still down");
        cleaner.Calls[0].ShouldBe(("the quick brown fox jumped", null));

        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        cleaner.Calls.Count.ShouldBe(2, "after the key-up only the tail goes to the cleaner");
        cleaner.Calls[1].ShouldBe(("over the lazy sleeping dog", "THE QUICK BROWN FOX JUMPED"));
        injector.Injected.ShouldHaveSingleItem().ShouldBe("THE QUICK BROWN FOX JUMPED OVER THE LAZY SLEEPING DOG");
    }

    [Fact]
    public async Task A_piece_the_cleaner_could_not_handle_falls_back_to_cleaning_everything()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Noise(WindowSeconds + 5);
        var transcriber = Piece_then_tail(capture);
        await transcriber.LoadAsync(CancellationToken.None);
        var injector = new RecordingTextInjector();
        var cleaner = new FailFirstCleaner();

        await using var engine = new DictationEngine(capture, hotkey, transcriber, injector, () => [])
        {
            AiCleanup = true,
            Cleaner = cleaner,
        };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        await Wait.UntilAsync(() => cleaner.Calls.Count == 1);

        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        cleaner.Calls[^1].ShouldBe("the quick brown fox jumped over the lazy sleeping dog", "the whole dictation is cleaned in one go");
        injector.Injected.ShouldHaveSingleItem().ShouldBe("THE QUICK BROWN FOX JUMPED OVER THE LAZY SLEEPING DOG");
    }

    /// <summary>Returns nothing for its first call, then upper-cases.</summary>
    private sealed class FailFirstCleaner : ITranscriptCleaner
    {
        public List<string> Calls { get; } = [];
        public string Name => "flaky";

        public Task<string?> CleanAsync(string text, CancellationToken cancellationToken)
        {
            Calls.Add(text);
            return Task.FromResult<string?>(Calls.Count == 1 ? null : text.ToUpperInvariant());
        }
    }
}
