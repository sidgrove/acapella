using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.App.Views;
using Murmur.Core;
using Shouldly;

namespace Murmur.AppTests;

/// <summary>
/// A new dictation is one new card, not a rebuild of every card: the rebuild was what the
/// next paste waited on.
/// </summary>
public sealed class HistoryListTests
{
    [AvaloniaFact]
    public void A_new_record_is_inserted_at_the_top_without_rebuilding_the_rest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-history-{Guid.NewGuid()}.jsonl");
        var store = new TranscriptStore(path);
        for (var i = 0; i < 5; i++) store.Add(new TranscriptRecord { Text = $"Dictation {i}" });
        var view = new TranscriptionsView(store);
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var list = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel).Content as StackPanel;
            list.ShouldNotBeNull();
            list.Children.Count.ShouldBe(6, "the day's heading, then five cards");
            var heading = list.Children[0];
            var previousTop = list.Children[1];

            store.Add(new TranscriptRecord { Text = "Dictation 5" });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            list.Children.Count.ShouldBe(7);
            list.Children[0].ShouldBeSameAs(heading, "a dictation on the same day goes under the heading already there");
            list.Children[2].ShouldBeSameAs(previousTop, "the existing cards are kept, not rebuilt");
        }
        finally { window.Close(); File.Delete(path); }
    }

    [AvaloniaFact]
    public void A_removal_still_rebuilds_the_list()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-history-{Guid.NewGuid()}.jsonl");
        var store = new TranscriptStore(path);
        for (var i = 0; i < 3; i++) store.Add(new TranscriptRecord { Text = $"Dictation {i}" });
        var view = new TranscriptionsView(store);
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var list = (StackPanel)view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel).Content!;
            store.Remove(store.Records[0].Id);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            list.Children.Count.ShouldBe(3, "the heading and the two left");
        }
        finally { window.Close(); File.Delete(path); }
    }
}

/// <summary>The day's band is the list's header: pinned at the top, the next day taking over as it arrives.</summary>
public sealed class PinnedBandTests
{
    [AvaloniaFact]
    public void The_band_of_the_day_at_the_top_is_pinned_and_the_next_day_takes_over()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-pinned-{Guid.NewGuid()}.jsonl");
        var store = new TranscriptStore(path);
        var yesterday = DateTimeOffset.Now.Date.AddDays(-1).AddHours(12);
        for (var i = 0; i < 12; i++) store.Add(new TranscriptRecord { At = yesterday.AddMinutes(i), Text = $"Yesterday {i}, a line or two so the list runs past the window." });
        for (var i = 0; i < 12; i++) store.Add(new TranscriptRecord { At = DateTimeOffset.Now.AddMinutes(-i - 1), Text = $"Today {i}, a line or two so the list runs past the window." });
        var view = new TranscriptionsView(store);
        var window = new Window { Content = view, Width = 800, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            view.PinnedDay.ShouldBe(DateTime.Today, "the newest day heads the list");

            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel);
            var list = (StackPanel)scroll.Content!;
            var second = list.Children.Where(c => c.IsVisible).Skip(1).First(c => c.GetType().Name == "DayBand");
            scroll.Offset = new Avalonia.Vector(0, second.Bounds.Y + 4);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            view.PinnedDay.ShouldBe(DateTime.Today.AddDays(-1), "yesterday's band takes over once it reaches the top");

            scroll.Offset = default;
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            view.PinnedDay.ShouldBe(DateTime.Today);
        }
        finally { window.Close(); File.Delete(path); }
    }
}

/// <summary>Today's wait from key-up to text, shown under the history.</summary>
public sealed class LatencyReadoutTests
{
    [Xunit.Fact]
    public void The_typical_and_slowest_wait_today_are_shown_and_other_days_left_out()
    {
        var today = new DateTime(2026, 9, 25);
        var at = new DateTimeOffset(today.AddHours(10));
        TranscriptRecord[] records =
        [
            new() { At = at, ProcessingSeconds = 0.8 },
            new() { At = at.AddMinutes(1), ProcessingSeconds = 1.0 },
            new() { At = at.AddMinutes(2), ProcessingSeconds = 1.6 },
            new() { At = at.AddDays(-1), ProcessingSeconds = 9 },
        ];

        var summary = TranscriptionsView.LatencySummary(records, today).ShouldNotBeNull();
        summary.ShouldContain("3 dictated");
        summary.ShouldContain("1.00 s");
        summary.ShouldContain("1.6 s");
        TranscriptionsView.LatencySummary([], today).ShouldBeNull();
    }
}
