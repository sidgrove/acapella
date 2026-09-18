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
            list.Children.Count.ShouldBe(5);
            var previousTop = list.Children[0];

            store.Add(new TranscriptRecord { Text = "Dictation 5" });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            list.Children.Count.ShouldBe(6);
            list.Children[1].ShouldBeSameAs(previousTop, "the existing cards are kept, not rebuilt");
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
            list.Children.Count.ShouldBe(2);
        }
        finally { window.Close(); File.Delete(path); }
    }
}
