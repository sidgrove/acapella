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
            var recent = view.GetVisualDescendants().OfType<Badge>().Single();
            var headerBottom = recent.TranslatePoint(default, view)!.Value.Y + recent.Bounds.Height;
            var viewportTop = scroll.TranslatePoint(default, view)!.Value.Y;
            (viewportTop - headerBottom).ShouldBeGreaterThanOrEqualTo(12, "the header gap must remain when cards scroll");
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
