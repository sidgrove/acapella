using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Views;
using Murmur.Core;
using Shouldly;

namespace Murmur.AppTests;

/// <summary>Long transcripts must wrap inside the viewport at restored window sizes.</summary>
public sealed class ResponsiveLayoutTests
{
    [AvaloniaFact]
    public void Settings_keeps_the_recording_toggle_in_the_main_window()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            window.UpdateLayout();
            var toggle = window.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().Single();
            window.ShowSettings();
            window.UpdateLayout();
            window.GetVisualDescendants().ShouldContain(toggle);
            toggle.IsEffectivelyVisible.ShouldBeTrue();
            toggle.Bounds.Height.ShouldBeGreaterThan(0);
        }
        finally { window.Close(); }
    }
    /// <summary>
    /// Nothing clips: the title row's search and "..." (and the dictionary's Add word) stay inside
    /// the page at every width, the sections that are not current folding to their marks to make room.
    /// </summary>
    [AvaloniaTheory]
    [Xunit.InlineData(640, true)]
    [Xunit.InlineData(700, null)]
    [Xunit.InlineData(760, null)]
    [Xunit.InlineData(900, false)]
    [Xunit.InlineData(1080, false)]
    public void The_title_row_folds_the_sections_rather_than_cutting_off_its_tools(int width, bool? folded)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"acapella-masthead-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var composition = Murmur.App.Composition.ForPreview(
            new AppSettings(Path.Combine(folder, "s.json")), new DictionaryFile(Path.Combine(folder, "d.txt")),
            new TranscriptStore(Path.Combine(folder, "t.jsonl")), new SuggestionStore(Path.Combine(folder, "g.json")));
        composition.Settings.Update(composition.Settings.Data with { HasOnboarded = true });
        composition.Transcripts.Add(new TranscriptRecord { Text = "One dictation, so the history has its tools.", RawText = "One dictation" });
        var window = new MainWindow(composition) { Width = width, Height = 480 };
        try
        {
            window.Show();
            foreach (var section in new[] { "Dictations", "Dictionary" })
            {
                var links = window.GetVisualDescendants().OfType<NavLink>().ToList();
                links.Single(l => l.Text == section).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }

                var current = links.Single(l => l.IsActive);
                current.Text.ShouldBe(section);
                current.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == section).IsVisible.ShouldBeTrue("the current section always keeps its name");
                if (section == "Dictations" && folded is { } expected) links.All(l => l.IsCompact == expected).ShouldBeTrue($"{section} at {width}");

                var page = window.GetVisualDescendants().OfType<WashPanel>().Single();
                var pageRight = page.TranslatePoint(default, window)!.Value.X + page.Bounds.Width;
                // The title row's tools are the buttons level with the sections.
                var rowY = current.TranslatePoint(new Point(0, current.Bounds.Height / 2), window)!.Value.Y;
                var tools = window.GetVisualDescendants().OfType<SgButton>()
                    .Where(b => b.IsEffectivelyVisible && Math.Abs(b.TranslatePoint(new Point(0, b.Bounds.Height / 2), window)!.Value.Y - rowY) < 12)
                    .ToList();
                tools.Count.ShouldBeGreaterThanOrEqualTo(2, "search and the more menu");
                foreach (var tool in tools)
                {
                    var right = tool.TranslatePoint(default, window)!.Value.X + tool.Bounds.Width;
                    right.ShouldBeLessThanOrEqualTo(pageRight - 16, $"a title-row tool is cut off on {section} at {width}");
                }
            }
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaTheory]
    [Xunit.InlineData(640, 480)]
    [Xunit.InlineData(780, 480)]
    [Xunit.InlineData(880, 660)]
    [Xunit.InlineData(1600, 900)]
    public void Long_transcripts_and_actions_fit_the_viewport(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sidders-layout-{Guid.NewGuid()}.jsonl");
        var store = new TranscriptStore(path);
        store.Add(new TranscriptRecord { Text = string.Join(" ", Enumerable.Repeat("A longer dictation should wrap cleanly inside its card.", 12)), RawText = "Original words", CleanedBy = "Gemini-2.5-Flash" });
        var view = new TranscriptionsView(store);
        var window = new MainWindow { Width = width, Height = height };
        try
        {
            window.Show();
            window.UpdateLayout();
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.GetType() == typeof(ContentControl));
            host.Content = view;
            window.Width = width;
            window.Height = height;
            window.UpdateLayout();
            foreach (var navigation in window.GetVisualDescendants().OfType<NavLink>())
            {
                var origin = navigation.TranslatePoint(default, window)!.Value;
                origin.X.ShouldBeGreaterThanOrEqualTo(0);
                (origin.X + navigation.Bounds.Width).ShouldBeLessThanOrEqualTo(window.Bounds.Width);
                (origin.Y + navigation.Bounds.Height).ShouldBeLessThanOrEqualTo(window.Bounds.Height);
            }
            if (Environment.GetEnvironmentVariable("ACAPELLA_VISUAL_DIR") is { } captureDir)
            {
                Directory.CreateDirectory(captureDir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(captureDir, $"history-{width}x{height}.png"));
            }
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel);
            foreach (var button in view.GetVisualDescendants().OfType<SgButton>())
            {
                var origin = button.TranslatePoint(default, view)!.Value;
                (origin.X + button.Bounds.Width).ShouldBeLessThanOrEqualTo(view.Bounds.Width);
            }
            scroll.Bounds.Height.ShouldBeGreaterThan(120, "the header must leave room to read history");
            scroll.Offset = new Vector(0, 80);
            window.UpdateLayout();
            // No toolbar strip: search sits on the title row, so the list starts straight under the status card.
            var status = window.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().Single();
            var statusBottom = status.TranslatePoint(default, window)!.Value.Y + status.Bounds.Height;
            scroll.TranslatePoint(default, window)!.Value.Y.ShouldBeGreaterThan(statusBottom, "the list never slides under the status card");
            var list = (StackPanel)scroll.Content!;
            foreach (var card in list.Children)
            {
                card.Bounds.Width.ShouldBeLessThanOrEqualTo(scroll.Viewport.Width);
                var cardOrigin = card.TranslatePoint(default, window)!.Value;
                (cardOrigin.X + card.Bounds.Width).ShouldBeLessThanOrEqualTo(window.Bounds.Width - 20);
            }
        }
        finally { window.Close(); File.Delete(path); }
    }
}
