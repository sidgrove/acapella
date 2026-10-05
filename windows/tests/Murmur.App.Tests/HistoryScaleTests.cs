using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.App.Views;
using Murmur.Core;
using Murmur.Dictionary;
using Shouldly;

namespace Murmur.AppTests;

/// <summary>A history the size of Dave's opens quickly: only the newest cards are built.</summary>
public sealed class HistoryScaleTests
{
    [AvaloniaFact]
    public void Thousands_of_dictations_open_without_building_every_card()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-scale-{Guid.NewGuid()}.jsonl");
        var store = new TranscriptStore(path);
        var start = DateTimeOffset.Now.AddDays(-20);
        for (var i = 0; i < 3300; i++)
        {
            store.Add(new TranscriptRecord
            {
                At = start.AddMinutes(i * 8),
                Text = $"Dictation number {i}, a sentence or two long, the way most of them are.",
                RawText = $"dictation number {i} a sentence or two long the way most of them are",
                App = (i % 4) switch { 0 => "claude", 1 => "slack", 2 => "OUTLOOK", _ => "chrome" },
                Style = "Prompt",
                CleanedBy = "gemini-2.5-flash",
                Corrections = i % 5 == 0 ? [new AppliedCorrection("yeah", "yeh", 1)] : null,
                AudioSeconds = 4,
                ProcessingSeconds = 0.9,
            });
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var view = new TranscriptionsView(store);
        var window = new Window { Content = view, Width = 1080, Height = 780 };
        try
        {
            window.Show();
            window.UpdateLayout();
            clock.Stop();

            var list = (StackPanel)view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel).Content!;
            list.Children.OfType<Border>().Count(b => b.Child is Grid).ShouldBeLessThanOrEqualTo(TranscriptionsView.PageSize, "only a page of rows (each day's band is a border too, so rows are counted by their grid)");
            clock.ElapsedMilliseconds.ShouldBeLessThan(3000, "opening the window must not wait on thousands of cards");

            var more = list.Children.OfType<Avalonia.Controls.Control>().SelectMany(c => c.GetVisualDescendants().Prepend(c)).OfType<Murmur.App.Controls.SgButton>()
                .Single(b => b.Content is string s && s.StartsWith("Show", StringComparison.Ordinal));
            more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            window.UpdateLayout();
            list.Children.OfType<Border>().Count(b => b.Child is Grid).ShouldBe(TranscriptionsView.PageSize * 2);
        }
        finally { window.Close(); File.Delete(path); }
    }
}
