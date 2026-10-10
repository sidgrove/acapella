using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.App;
using Murmur.App.Views;
using Murmur.Core;
using Murmur.Dictionary;
using Shouldly;

namespace Murmur.AppTests;

/// <summary>
/// Renders every screen against sample data and saves it as a PNG, so a design change can be
/// looked at, before and after, without a person driving the app. Does nothing unless
/// ACAPELLA_VISUAL_DIR names a folder.
/// </summary>
public sealed class VisualTourTests
{
    private static readonly string? Dir = Environment.GetEnvironmentVariable("ACAPELLA_VISUAL_DIR");

    [AvaloniaFact]
    public void Every_screen_is_rendered_for_review()
    {
        if (Dir is null) return;
        Directory.CreateDirectory(Dir);

        var folder = Path.Combine(Path.GetTempPath(), $"acapella-tour-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            Murmur.App.Controls.AppMark.CacheFolder = Path.Combine(folder, "icons");
            // The platform layer isn't beside the test host, so real icons can't be read here;
            // icons read on this machine can be seeded so the shots look as the app will.
            if (Environment.GetEnvironmentVariable("ACAPELLA_ICON_SEED") is { } seed && Directory.Exists(seed))
            {
                Directory.CreateDirectory(Murmur.App.Controls.AppMark.CacheFolder);
                foreach (var file in Directory.GetFiles(seed, "*.png")) File.Copy(file, Path.Combine(Murmur.App.Controls.AppMark.CacheFolder, Path.GetFileName(file).ToLowerInvariant()));
            }
            var composition = Sample(folder);

            var main = new MainWindow(composition) { Width = 1080, Height = 780 };
            main.Show();
            Save(main, "history-1080");
            // Scrolled: the day's band stays pinned at the top of the card, and yesterday's takes over.
            var history = main.GetVisualDescendants().OfType<TranscriptionsView>().Single();
            var rows = history.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel);
            var yesterdayBand = ((StackPanel)rows.Content!).Children.Where(c => c.IsVisible && c.GetType().Name == "DayBand").First();
            foreach (var (offset, shot) in new[] { (260.0, "history-scrolled"), (yesterdayBand.Bounds.Y - 120, "history-scrolled-arriving") })
            {
                rows.Offset = new Avalonia.Vector(0, offset);
                Save(main, shot);
            }
            // Shorter, so the list can carry yesterday's band all the way to the top.
            main.Height = 560;
            Save(main, "history-scrolled-yesterday");
            rows.Offset = new Avalonia.Vector(0, yesterdayBand.Bounds.Y + 40);
            Save(main, "history-scrolled-yesterday");
            main.Height = 780;
            rows.Offset = default;
            main.Width = 640; main.Height = 480;
            Save(main, "history-640");
            main.Width = 1440; main.Height = 900;
            Save(main, "history-1440");
            main.Width = 1080; main.Height = 780;

            main.ToggleRecording();
            Save(main, "history-listening");
            main.ToggleRecording();
            composition.Settings.Update(composition.Settings.Data with { IsEnabled = false });
            main.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().First().IsChecked = false;
            Save(main, "history-paused");
            main.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().First().IsChecked = true;

            main.ReportFault("AI clean-up did not respond, so the local transcript was typed. Check the key and connection in Settings.");
            Save(main, "history-fault");
            main.FaultNotice.IsVisible = false;

            Click(main, "Dictionary");
            Save(main, "dictionary-1080");
            main.Width = 640; main.Height = 480;
            Save(main, "dictionary-640");
            main.Width = 1080; main.Height = 780;

            main.ShowSettings();
            Save(main, "settings-1080");
            foreach (var (offset, name) in new[] { (700.0, "settings-2"), (1500.0, "settings-3"), (2400.0, "settings-4"), (3400.0, "settings-5") })
            {
                var scroll = main.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Content is StackPanel or Border);
                scroll.Offset = new Avalonia.Vector(0, offset);
                Save(main, name);
            }
            main.Width = 640; main.Height = 480;
            Save(main, "settings-640");
            main.Close();

            var empty = Composition.ForPreview(
                new AppSettings(Path.Combine(folder, "s2.json")), new DictionaryFile(Path.Combine(folder, "d2.txt")),
                new TranscriptStore(Path.Combine(folder, "t2.jsonl")), new SuggestionStore(Path.Combine(folder, "g2.json")));
            empty.Settings.Update(empty.Settings.Data with { HasOnboarded = true });
            var blank = new MainWindow(empty) { Width = 1080, Height = 780 };
            blank.Show();
            Save(blank, "history-empty");
            blank.Close();

            var overlay = new OverlayWindow(null);
            overlay.Show();
            overlay.Sync(true, false, false, 0.5, "00:02", "");
            Save(overlay, "overlay-listening-quiet");
            overlay.Sync(true, false, false, 0.5, "00:07", "So I think the thing with the cash flow page is it's starting to feel genuinely joyful");
            Save(overlay, "overlay-listening");
            overlay.Sync(true, false, false, 0.5, "00:31", string.Join(" ", Enumerable.Repeat("So I think the thing with the cash flow page is it's starting to feel genuinely joyful.", 4)));
            Save(overlay, "overlay-listening-long");
            overlay.Sync(false, true, true, 0, "00:09", "");
            Save(overlay, "overlay-cleaning");
            overlay.Sync(false, true, false, 0, "00:09", "");
            Save(overlay, "overlay-writing");
            overlay.ShowDone();
            Save(overlay, "overlay-done");
            overlay.ShowDone(asHeard: true);
            Save(overlay, "overlay-as-heard");
            overlay.ShowNotice("Nothing heard. Is the mic muted?");
            Save(overlay, "overlay-notice");
            overlay.ShowSendFeedback();
            Save(overlay, "overlay-sent");
            overlay.Close();
            Sheet("overlay-states", ["overlay-listening-quiet", "overlay-listening", "overlay-cleaning", "overlay-writing", "overlay-done", "overlay-as-heard", "overlay-notice", "overlay-sent"], columns: 2, ground: Avalonia.Media.Brushes.Black);

            var welcome = new WelcomeWindow(composition);
            welcome.Show();
            Save(welcome, "welcome-1");
            Click(welcome, "Next", button: true);
            Save(welcome, "welcome-2");
            Click(welcome, "Next", button: true);
            Save(welcome, "welcome-3");
            welcome.Close();

            var editor = new DictionaryEditorWindow(DictionaryEntry.Correction("Quilla", "Quiller"));
            editor.Show();
            Save(editor, "dictionary-editor");
            editor.Close();

            var about = new AboutWindow();
            about.Show();
            Save(about, "about");
            about.Close();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The thumb and the tab glide, captured frame by frame with the clock stepped by hand, so the
    /// motion can be seen without a person at the app. Writes glide-NN.png beside the tour.
    /// </summary>
    [AvaloniaFact]
    public void The_glides_are_rendered_frame_by_frame()
    {
        if (Dir is null) return;
        Directory.CreateDirectory(Dir);
        var animate = Murmur.App.Design.Tokens.Motion.Animate;
        Murmur.App.Design.Tokens.Motion.Animate = true;
        try
        {
            var links = new[] { new Murmur.App.Controls.NavLink("Dictations") { IsActive = true }, new Murmur.App.Controls.NavLink("Dictionary"), new Murmur.App.Controls.NavLink("Settings") };
            var tabs = Murmur.App.Controls.NavLink.Track(links);
            var mode = new Murmur.App.Controls.Segmented(
            [
                new Murmur.App.Controls.Segmented.Choice("Instant", Murmur.App.Controls.Icons.Zap, Murmur.App.Design.Tokens.Accent.Amber),
                new Murmur.App.Controls.Segmented.Choice("Polished", Murmur.App.Controls.Icons.Sparkles, Murmur.App.Design.Tokens.Accent.Plum),
            ]);
            var window = new Window
            {
                Width = 420, Height = 120, Background = Murmur.App.Design.Tokens.Brushes.Card,
                Content = new StackPanel { Spacing = 16, Margin = new Avalonia.Thickness(20), Children = { tabs, mode } },
            };
            window.Show();
            Save(window, "glide-00");
            links[0].IsActive = false;
            links[2].IsActive = true;
            mode.Press(1);
            for (var frame = 1; frame <= 24; frame++)
            {
                // The transition clock follows real time, so each frame waits one frame of it.
                Thread.Sleep(16);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Save(window, $"glide-{frame:00}");
            }
            window.Close();
        }
        finally { Murmur.App.Design.Tokens.Motion.Animate = animate; }
    }

    /// <summary>
    /// The pill's life with motion on, frame by frame in real time: rising in, listening, the
    /// loader's dots, the tick drawing itself in, and the fade away. Writes pill-NN-what.png.
    /// </summary>
    [AvaloniaFact]
    public void The_pill_is_rendered_frame_by_frame()
    {
        if (Dir is null) return;
        Directory.CreateDirectory(Dir);
        var animate = Murmur.App.Design.Tokens.Motion.Animate;
        Murmur.App.Design.Tokens.Motion.Animate = true;
        try
        {
            var overlay = new OverlayWindow(null) { Background = Murmur.App.Design.Tokens.Canvas.Base };
            var shot = 0;
            void Run(string what, int frames, int every = 1, Func<bool>? until = null)
            {
                for (var frame = 0; frame < frames && until?.Invoke() != true; frame++)
                {
                    Thread.Sleep(16);
                    // With no message loop the dispatcher only looks at its timers when it has a job
                    // to run, so each frame hands it one; the pill's own timers then tick in real time.
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    if (frame % every == 0) Save(overlay, $"pill-{shot++:00}-{what}");
                }
            }

            overlay.Present();
            overlay.Sync(true, false, false, 0.5, "00:00", "");
            Save(overlay, $"pill-{shot++:00}-in");
            Run("in", 14, 2);
            overlay.Sync(true, false, false, 0.6, "00:04", "It's starting to feel genuinely joyful");
            Run("listening", 6, 3);
            overlay.Sync(false, true, true, 0, "00:05", "It's starting to feel genuinely joyful");
            Run("working", 24, 4);
            overlay.ShowDone();
            Run("done", 120, 3, until: () => !overlay.IsShowingDone);
            overlay.IsShowingDone.ShouldBeFalse("the tick lasts its moment and no longer");
            overlay.IsLeaving.ShouldBeTrue("and then the pill fades rather than vanishing");
            Run("out", 60, 1, until: () => !overlay.IsVisible);
            overlay.IsVisible.ShouldBeFalse("the pill hides once it has faded");
            overlay.Close();
            Sheet("pill-frames", [.. Directory.GetFiles(Dir, "pill-??-*.png").Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal)], columns: 4);
        }
        finally { Murmur.App.Design.Tokens.Motion.Animate = animate; }
    }

    /// <summary>Tiles shots already saved into one contact sheet, so a set is judged side by side at real size.</summary>
    private static void Sheet(string name, IReadOnlyList<string> shots, int columns, Avalonia.Media.IBrush? ground = null)
    {
        var tiles = shots.Select(s => new Avalonia.Media.Imaging.Bitmap(Path.Combine(Dir!, $"{s}.png"))).ToList();
        try
        {
            var cell = new Avalonia.PixelSize(tiles.Max(t => t.PixelSize.Width), tiles.Max(t => t.PixelSize.Height));
            var rows = (tiles.Count + columns - 1) / columns;
            using var sheet = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(cell.Width * columns, cell.Height * rows));
            using (var context = sheet.CreateDrawingContext())
            {
                context.FillRectangle(ground ?? Murmur.App.Design.Tokens.Canvas.Base, new Avalonia.Rect(0, 0, cell.Width * columns, cell.Height * rows));
                for (var i = 0; i < tiles.Count; i++)
                {
                    var size = tiles[i].PixelSize;
                    context.DrawImage(tiles[i], new Avalonia.Rect((i % columns) * cell.Width, (i / columns) * cell.Height, size.Width, size.Height));
                }
            }
            sheet.Save(Path.Combine(Dir!, $"{name}.png"));
        }
        finally
        {
            foreach (var tile in tiles) tile.Dispose();
        }
    }

    private static void Save(Window window, string name)
    {
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(Dir!, $"{name}.png"));
    }

    private static void Click(Window window, string text, bool button = false)
    {
        Avalonia.Controls.Button? target = button
            ? window.GetVisualDescendants().OfType<Murmur.App.Controls.SgButton>().FirstOrDefault(b => Equals(b.Content, text) && b.IsEffectivelyVisible)
            : window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith(text, StringComparison.Ordinal) == true));
        target?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    }

    private static Composition Sample(string folder)
    {
        var settings = new AppSettings(Path.Combine(folder, "settings.json"));
        settings.Update(settings.Data with { HasOnboarded = true, AiCleanup = true, CustomInstructions = "Use English spellings\nDon't put commas before and" });

        var dictionaryPath = Path.Combine(folder, "dictionary.txt");
        File.WriteAllText(dictionaryPath, """
            Xero
            PAYE
            Sidgrove
            Claude
            Quiller
            Quilla -> Quiller
            git pool -> git pull
            get pole -> git pull
            cloud code -> Claude Code
            Sarif -> serif
            Gev -> Jev
            yeah -> yeh
            Cork Tax -> Corp Tax
            # off: Sid grove -> Sidgrove
            accruals
            prepayments
            """);
        var dictionary = new DictionaryFile(dictionaryPath);

        var suggestions = new SuggestionStore(Path.Combine(folder, "suggestions.json"));
        var now = DateTimeOffset.Now;
        suggestions.Offer("Hoxon", "Hoxton", "…a coffee in Hoxton before the meeting…", now);
        suggestions.Offer("Pay Circle", "PayCircle", "…through his PayCircle login…", now);
        var learnt = suggestions.Offer("Reflect", "Reflekt", "…what's been done in Reflekt just for comparison…", now.AddDays(-2));
        dictionary.Add(DictionaryEntry.Correction("Reflect", "Reflekt"));
        suggestions.MarkLearnt(learnt.Id, now.AddDays(-2), "you made this fix 2 times");

        var transcripts = new TranscriptStore(Path.Combine(folder, "transcripts.jsonl"));
        var today = DateTimeOffset.Now.Date;
        void Add(int minutesAgo, string text, string? raw = null, string? app = null, string? style = null, double audio = 4, double secs = 0.9,
            IReadOnlyList<AppliedCorrection>? corrections = null, string? edited = null, bool failed = false, string? by = "gemini-2.5-flash") =>
            transcripts.Add(new TranscriptRecord
            {
                At = DateTimeOffset.Now.AddMinutes(-minutesAgo), Text = text, RawText = raw ?? text, App = app, Style = style,
                AudioSeconds = audio, ProcessingSeconds = secs, Corrections = corrections, EditChecked = edited is not null, EditedText = edited,
                CleanupFailed = failed, CleanedBy = failed ? null : by, TranscribedBy = "scribe_v2_realtime",
            });
        var yesterday = (int)(DateTime.Now - DateTime.Today).TotalMinutes;
        Add(yesterday + 320, "Let's run the Bible over the whole interface", app: "claude", style: "Prompt", audio: 2.1, secs: 0.7);
        Add(yesterday + 260, "Right, let's go through the VAT review together on Thursday morning, if that works for you", app: "OUTLOOK", style: "Email", audio: 5.2, secs: 0.9);
        Add(yesterday + 200, "Can you send the payroll journal over for Circle once it's posted?", app: "slack", style: "Chat", audio: 3.4, secs: 0.8);
        Add(240, "Okay, so the US entity was non-existent. I presume it would need to be in existence in order for there to be a contract with it", app: "slack", style: "Chat", audio: 9.2, secs: 1.1);
        Add(200, "Can we run Impeccable over the serif headings on the pricing page please?", raw: "Can we run impeccable over the Sarif headings on the pricing page please", app: "claude", style: "Prompt", corrections: [new AppliedCorrection("Sarif", "serif", 1)]);
        Add(120, "I've now pushed Xero down to the basic tier, so it's just £2.50 a month basically", app: "OUTLOOK", style: "Email", audio: 6.1, secs: 0.8, edited: "I've now pushed Xero down to the basic tier; so it's just £2.50 a month basically");
        Add(60, "Thanks mate, that's brilliant", by: null, failed: true, audio: 1.2, secs: 0.3);
        Add(30, "I do think you're starting to absolutely nail what it means to be aligning to the Sidgrove bible though. It's the way you've used different shades of line to emphasise headers, the icons, the different colours and the logos. It's all of those things.", app: "claude", style: "Prompt", audio: 21.4, secs: 1.4);
        Add(8, "And yeh, do any of the other ones you need to do", raw: "And yeah, do any of the other ones you need to do", app: "claude", style: "Prompt", audio: 2.3, secs: 0.67, corrections: [new AppliedCorrection("yeah", "yeh", 1)]);
        Add(5, "ElevenLabs API", audio: 1.7, secs: 0.99);

        return Composition.ForPreview(settings, dictionary, transcripts, suggestions);
    }
}
