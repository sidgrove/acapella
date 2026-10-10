using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Design;

namespace Murmur.AppTests.BibleAudit;

/// <summary>What the options for the status card and the fault share.</summary>
internal static class StatusKit
{
    /// <summary>The fault used on every row of the sheet, as the app words it today.</summary>
    public const string Fault = "AI clean-up did not respond, so the local transcript was typed. Check the key and connection in Settings.";

    /// <summary>
    /// The fault as one small chip on the line it is about, the whole sentence in its tooltip
    /// (the Bible: "A warning is a small chip on the line it is about, never a full-width bar").
    /// Coral, the house hue for failed; red stays with recording.
    /// </summary>
    public static Chip FaultChip()
    {
        var chip = new Chip("Clean-up did not respond", Tokens.Accent.Coral, Icons.Alert);
        ToolTip.SetTip(chip, Fault);
        return chip;
    }
}

/// <summary>
/// A, "Quiet until it matters": the status card is taken away. Ready is how the app nearly always
/// is, the pill already shows listening wherever the caret is, and the tray pauses it; so the
/// window says nothing at rest and one chip beside the title when something is wrong or paused.
/// </summary>
internal static class StatusA
{
    public const string Name = "A  Quiet until it matters";
    public const string Line = "The card goes. Nothing at rest; one chip beside the title when it is paused or something failed.";

    public static void Place(Chrome chrome, bool fault)
    {
        chrome.Status.IsVisible = false;
        if (!fault) return;
        var chip = StatusKit.FaultChip();
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        chip.Margin = new Thickness(Tokens.Space.Base, Tokens.Space.Tight, 0, 0);
        Grid.SetColumn(chip, 2);
        chrome.Masthead.Children.Add(chip);
    }
}

/// <summary>
/// B, "Only on its own page": the status card stays where it is the page's answer, on Dictations,
/// and leaves Dictionary and Settings, where it is the same card a second and third time. A fault
/// is a chip on the card's own line, never a bar under it.
/// </summary>
internal static class StatusB
{
    public const string Name = "B  Only on its own page";
    public const string Line = "The card stays on Dictations and leaves the other two sections. A fault is a chip on its line.";

    public static void Place(Chrome chrome, bool onDictations, bool fault)
    {
        chrome.Status.IsVisible = onDictations;
        if (!onDictations || !fault) return;
        chrome.Window.UpdateLayout();
        var state = chrome.Status.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Ready");
        var words = (StackPanel)((StackPanel)state.Parent!).Parent!;
        var chip = StatusKit.FaultChip();
        chip.Margin = new Thickness(Tokens.Space.Snug, 0, 0, 0);
        words.Children.Add(chip);
    }
}

/// <summary>
/// C, "In the window's own strip": the state, the key and the pause switch move up into the
/// caption strip, which today holds the mark and forty pixels of nothing. It is there on every
/// section and costs the page no height at all.
/// </summary>
internal static class StatusC
{
    public const string Name = "C  In the window's own strip";
    public const string Line = "State, key and pause move into the empty caption strip: on every section, and no height taken from the page.";

    public static void Place(Chrome chrome, bool fault)
    {
        chrome.Status.IsVisible = false;
        var state = new TextBlock
        {
            Text = "Ready",
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Label,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new IconTile(Icons.Mic, Tokens.Accent.Brand, Tokens.Layout.TileSmall), state, KeyCaps.Make("Right Ctrl") },
        };
        if (fault) row.Children.Add(StatusKit.FaultChip());
        row.Children.Add(new Switch { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Tokens.Space.Tight, 0, 0, 0) });

        // The caption's right-hand run: whatever trails, then the window's own three buttons.
        var buttons = (StackPanel)chrome.Window.GetVisualDescendants().OfType<CaptionGlyph>().First().Parent!;
        ((StackPanel)buttons.Parent!).Children.Insert(0, row);
    }
}
