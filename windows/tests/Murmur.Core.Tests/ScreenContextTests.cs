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

/// <summary>The clean-up is shown what is on screen, for spelling, and only when allowed.</summary>
public sealed class ScreenContextTests
{
    private sealed class ScreenCleaner : ITranscriptCleaner
    {
        public List<ScreenContext?> Screens { get; } = [];
        public string Name => "screen";

        public Task<string?> CleanAsync(string text, CancellationToken cancellationToken) => Task.FromResult<string?>(text);

        public Task<string?> CleanWithScreenAsync(string text, ScreenContext? screen, CancellationToken cancellationToken)
        {
            lock (Screens) Screens.Add(screen);
            return Task.FromResult<string?>(text);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var reply = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = "Thanks Siobhan" } } } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    private static async Task<(ScreenCleaner Cleaner, RecordingTextInjector Injector)> DictateAsync(bool seesScreen)
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector
        {
            CaretText = "Hi Dave, Siobhan here about the management pack.\n\n",
            ForegroundWindow = new FocusedWindow("OUTLOOK", "RE: Management pack - Inbox"),
        };
        var cleaner = new ScreenCleaner();
        var capture = FakeAudioCapture.Tone(0.8);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("thanks shivon"), injector, () => [])
        {
            AiCleanup = true,
            Cleaner = cleaner,
            CleanupSeesScreen = seesScreen,
        };

        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle && injector.Injected.Count > 0);
        return (cleaner, injector);
    }

    [Fact]
    public async Task The_cleaner_sees_the_app_the_window_and_the_text_before_the_caret()
    {
        var (cleaner, _) = await DictateAsync(seesScreen: true);

        var screen = cleaner.Screens.ShouldHaveSingleItem().ShouldNotBeNull();
        screen.Window.ShouldBe(new FocusedWindow("OUTLOOK", "RE: Management pack - Inbox"));
        screen.BeforeCaret.ShouldNotBeNull().ShouldContain("Siobhan");
    }

    [Fact]
    public async Task Switched_off_the_cleaner_sees_nothing()
    {
        var (cleaner, _) = await DictateAsync(seesScreen: false);

        cleaner.Screens.ShouldHaveSingleItem().ShouldBeNull();
    }

    [Fact]
    public void The_screen_is_framed_as_context_and_trimmed_from_the_far_end()
    {
        var before = new string('x', 1500) + " Siobhan";
        var framed = GeminiCleaner.Screen(new ScreenContext(new FocusedWindow("OUTLOOK", "RE: Management pack"), before));

        framed.ShouldContain("App: OUTLOOK");
        framed.ShouldContain("Window: RE: Management pack");
        framed.ShouldContain("Siobhan", Case.Sensitive);
        framed.ShouldContain("Never repeat it");
        framed.ShouldEndWith("The dictation to clean:\n");
        framed.Length.ShouldBeLessThan(GeminiCleaner.ScreenCharacters + 600);
        GeminiCleaner.Screen(null).ShouldBeEmpty();
        GeminiCleaner.Screen(new ScreenContext(null, "  ")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_screen_goes_ahead_of_the_dictation_in_the_request()
    {
        var handler = new CapturingHandler();
        using var cleaner = new GeminiCleaner(() => "test-key", null, handler);

        await cleaner.CleanWithScreenAsync("thanks shivon", new ScreenContext(new FocusedWindow("OUTLOOK", "Inbox"), "Siobhan here"), CancellationToken.None);

        var body = JsonDocument.Parse(handler.LastBody).RootElement
            .GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString().ShouldNotBeNull();
        body.IndexOf("Siobhan here", StringComparison.Ordinal).ShouldBeLessThan(body.IndexOf("thanks shivon", StringComparison.Ordinal));
        body.ShouldEndWith("thanks shivon");
    }
}
