using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Views;
using Murmur.Core;
using Shouldly;

namespace Murmur.AppTests;

/// <summary>The dictionary is one list, sorted by what gets written.</summary>
public sealed class DictionaryListTests
{
    private const string Body = """
        Xero
        git pool -> git pull
        Anthropic
        cloud code -> Claude Code
        get poll -> git pull
        # off: Sid grove -> Sidgrove
        accruals
        """;

    [AvaloniaTheory]
    [Xunit.InlineData(640, 480)]
    [Xunit.InlineData(1080, 780)]
    public void Entries_sit_in_one_panel_sorted_by_what_is_written(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-dictionary-{Guid.NewGuid()}.txt");
        File.WriteAllText(path, Body);
        var view = new DictionaryView(new DictionaryFile(path));
        var window = new MainWindow { Width = width, Height = height };
        try
        {
            window.Show();
            window.UpdateLayout();
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.GetType() == typeof(ContentControl));
            host.Content = view;
            window.UpdateLayout();

            if (Environment.GetEnvironmentVariable("ACAPELLA_VISUAL_DIR") is { } captureDir)
            {
                Directory.CreateDirectory(captureDir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(captureDir, $"dictionary-{width}x{height}.png"));
            }

            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel);
            var list = (StackPanel)scroll.Content!;
            list.Children.Count.ShouldBe(1, "every entry belongs in a single panel, not a card each");
            list.Children[0].Bounds.Width.ShouldBeLessThanOrEqualTo(scroll.Viewport.Width);

            var switches = view.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().ToList();
            switches.Count.ShouldBe(7);
            switches.Count(s => s.IsChecked == false).ShouldBe(1);

            var written = list.Children[0].GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.FontWeight == Avalonia.Media.FontWeight.Bold)
                .Select(t => t.Text)
                .ToList();
            written.ShouldBe(["accruals", "Anthropic", "Claude Code", "git pull", "git pull", "Sidgrove", "Xero"]);

            view.GetVisualDescendants().OfType<SgButton>()
                .Where(b => Equals(b.Content, "Delete"))
                .ShouldAllBe(b => !b.IsVisible, "Delete shows only on the row under the pointer");
        }
        finally { window.Close(); File.Delete(path); }
    }

    [AvaloniaFact]
    public void Suggestions_from_edits_sit_above_the_list_and_Add_moves_one_into_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acapella-dictionary-{Guid.NewGuid()}.txt");
        var suggestionsPath = Path.Combine(Path.GetTempPath(), $"acapella-suggestions-{Guid.NewGuid()}.json");
        File.WriteAllText(path, Body);
        var file = new DictionaryFile(path);
        var suggestions = new SuggestionStore(suggestionsPath);
        var at = DateTimeOffset.Now;
        suggestions.Offer("Sarif", "serif", "…run Impeccable over the serif headings on the pricing page…", at);
        suggestions.Offer("Sarif", "serif", null, at);
        suggestions.Offer("Gev", "Jev", null, at);
        suggestions.Offer("git pool", "git pull", null, at);   // already in the dictionary

        var view = new DictionaryView(file, suggestions);
        var window = new MainWindow { Width = 1080, Height = 780 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.GetType() == typeof(ContentControl));
            host.Content = view;
            window.UpdateLayout();

            if (Environment.GetEnvironmentVariable("ACAPELLA_VISUAL_DIR") is { } captureDir)
            {
                Directory.CreateDirectory(captureDir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(captureDir, "dictionary-suggestions.png"));
            }

            DictionaryView.PendingSuggestions(suggestions, file).ShouldBe(2);
            var list = (StackPanel)view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel).Content!;
            list.Children.Count.ShouldBe(2, "the suggestions panel, then the dictionary");

            var add = list.Children[0].GetVisualDescendants().OfType<SgButton>().First(b => Equals(b.Content, "Add"));
            add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

            file.Entries.ShouldContain(e => e.Hear == "Sarif" && e.Write == "serif");
            suggestions.Pending.ShouldNotContain(s => s.Hear == "Sarif");
        }
        finally { window.Close(); File.Delete(path); File.Delete(suggestionsPath); }
    }
}
