using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.Abstractions;
using Murmur.App;
using Murmur.App.Controls;
using Murmur.App.Views;
using Murmur.Core;
using Murmur.Core.Sync;
using Shouldly;
using Xunit;

namespace Murmur.AppTests;

/// <summary>The Sync card in Settings, and the byline under the wordmark.</summary>
public sealed class SyncCardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"acapella-synccard-{Guid.NewGuid():N}");

    public SyncCardTests()
    {
        Directory.CreateDirectory(_folder);
        Log.Path = Path.Combine(Path.GetTempPath(), $"murmur-tests-{Environment.ProcessId}.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private (Composition Composition, SyncAccount Account) Build()
    {
        var settings = new AppSettings(Path.Combine(_folder, "settings.json"));
        settings.Update(settings.Data with { HasOnboarded = true });
        var dictionary = new DictionaryFile(Path.Combine(_folder, "dictionary.txt"));
        var suggestions = new SuggestionStore(Path.Combine(_folder, "suggestions.json"));
        var transcripts = new TranscriptStore(Path.Combine(_folder, "transcripts.jsonl"));
        var account = new SyncAccount(Path.Combine(_folder, "sync-token.bin"), new PlainSecrets());
        var server = new SilentServer();
        var engine = new SyncEngine(server, Path.Combine(_folder, "sync-state.json"), "PC", dictionary, suggestions, transcripts, settings, null);
        var sync = new SyncService(engine, server, account, settings, _ => Task.FromResult(new SignInResult("t", "dave@sidgrove.com", null)));
        return (Composition.ForPreview(settings, dictionary, transcripts, suggestions, sync), account);
    }

    [AvaloniaFact]
    public void Signed_out_offers_sign_in()
    {
        var (composition, _) = Build();
        var part = new SyncPart(composition.Sync);
        var window = new Window { Content = part, Width = 800, Height = 400 };
        window.Show();

        Texts(part).ShouldContain("Not signed in");
        var signIn = Buttons(part).Single(b => Equals(b.Content, "Sign in with Sidgrove"));
        signIn.IsVisible.ShouldBeTrue();
        signIn.IsEnabled.ShouldBeTrue();
        Buttons(part).Single(b => Equals(b.Content, "Sign out")).IsVisible.ShouldBeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Signed_in_shows_the_account_and_when_it_last_synced()
    {
        var (composition, account) = Build();
        account.Save("t", "dave@sidgrove.com");
        var part = new SyncPart(composition.Sync);
        var window = new Window { Content = part, Width = 800, Height = 400 };
        window.Show();

        Texts(part).ShouldContain("Signed in as dave@sidgrove.com");
        Texts(part).ShouldContain("· not synced yet");
        Buttons(part).Single(b => Equals(b.Content, "Sync now")).IsVisible.ShouldBeTrue();
        Buttons(part).Single(b => Equals(b.Content, "Sign out")).IsVisible.ShouldBeTrue();
        Buttons(part).Single(b => Equals(b.Content, "Sign in with Sidgrove")).IsVisible.ShouldBeFalse();
        if (Environment.GetEnvironmentVariable("ACAPELLA_VISUAL_DIR") is { } dir)
        {
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "sync-signed-in.png"));
        }
        window.Close();
    }

    [AvaloniaFact]
    public void The_settings_page_has_a_sync_card_and_the_header_no_name_or_byline()
    {
        var (composition, _) = Build();
        var main = new MainWindow(composition) { Width = 1080, Height = 780 };
        main.Show();

        // The title row is the icon alone (Dave, 05/10/2026): no name and no byline.
        var texts = main.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.ShouldNotContain("by Sidgrove Intelligence");
        texts.ShouldNotContain("Acapella");
        main.ShowSettings();
        main.UpdateLayout();
        main.GetVisualDescendants().OfType<SyncPart>().ShouldHaveSingleItem();
        main.Close();
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(60 * 4, "4 minutes ago")]
    [InlineData(60 * 60 * 2, "2 hours ago")]
    public void Relative_times_read_naturally(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);
        SyncPart.Relative(now.AddSeconds(-secondsAgo), now).ShouldBe(expected);
    }

    [Fact]
    public void Older_than_a_day_is_a_british_date()
    {
        var now = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);
        SyncPart.Relative(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), now).ShouldBe("on 28/09/2026");
        SyncPart.Relative(null, now).ShouldBe("never");
    }

    private static List<string?> Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    private static List<SgButton> Buttons(Control root) => root.GetVisualDescendants().OfType<SgButton>().ToList();

    private sealed class PlainSecrets : ISecretStore
    {
        public byte[] Protect(byte[] plain) => plain;
        public byte[]? Unprotect(byte[] cipher) => cipher;
    }

    private sealed class SilentServer : ISyncServer
    {
        public Task<SyncReply> SyncAsync(SyncRequest request, CancellationToken cancellationToken) => Task.FromResult(new SyncReply());
        public Task<Uri?> RecordingUrlAsync(string key, RecordingTransfer transfer, CancellationToken cancellationToken) => Task.FromResult<Uri?>(null);
        public Task UploadAsync(Uri url, byte[] wav, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
        public Task RevokeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
