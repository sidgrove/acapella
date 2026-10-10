using Avalonia;
using Murmur.App.Controls;

namespace Murmur.AppTests.BibleAudit;

/// <summary>
/// D, "A quiet strip under the title": the Bible's other pattern for moving between sibling pages
/// (<c>PillNav variant="quiet"</c> with identity tiles, the month-end trail of 06/10/2026). The
/// title row is the title and the page's tools alone; the sections stand in one anchored strip
/// from the left edge beneath it, each a soft button with a 26px tile, the current one white.
/// </summary>
internal static class SectionNavD
{
    public const string Name = "D  A quiet strip under the title";
    public const string Line = "The sections leave the title row for one strip from the left beneath it. Bigger tiles, every word at any width.";

    public static SectionGroup Build() => new(4,
        new HeldButton("Dictations", Icons.Mic, House.Brand, House.QuietPage),
        new HeldButton("Dictionary", Icons.Book, House.Green, House.QuietPage, waiting: 2, onCorner: true),
        new HeldButton("Settings", Icons.Sliders, House.Purple, House.QuietPage));

    public static void Place(Chrome chrome, int current = 0)
    {
        var sections = Build();
        sections.Choose(current);
        chrome.Masthead.Children.Remove(chrome.Tabs);
        // The title row's own gap below gives way to the strip's, so the status card stays one
        // rhythm beneath whatever stands above it; the strip's left edge is the cards' left edge.
        var gap = chrome.Masthead.Margin;
        chrome.Masthead.Margin = new Thickness(gap.Left, gap.Top, gap.Right, 10);
        sections.Margin = new Thickness(chrome.Status.Margin.Left, 0, chrome.Status.Margin.Right, gap.Bottom);
        chrome.AddRowUnderTitle(sections);
    }
}
