using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Murmur.App.Design;

namespace Murmur.App.Controls;

/// <summary>
/// Search as a quiet magnifier on the title row that opens into the field when clicked (or on
/// Ctrl+F) and folds back once it is empty and left. A list that is mostly read, not searched,
/// keeps its room for the list.
/// </summary>
public sealed class SearchToggle : Panel
{
    private readonly SgButton _icon;

    /// <summary>Wraps <paramref name="box"/>, which the view keeps and listens to.</summary>
    public SearchToggle(TextBox box)
    {
        Box = box;
        // Open, the field is the bed's white thumb: the same height, corner and lift.
        box.Width = Tokens.Layout.SearchWidthNarrow + Tokens.Space.Wide;
        box.Height = Tokens.Layout.SegmentHeight;
        box.MinHeight = 0;
        box.Padding = new Thickness(Tokens.Space.Tight, 0, Tokens.Space.Snug, 0);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.CornerRadius = new CornerRadius(Tokens.Radius.Segment);
        box.BorderThickness = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        box.FontSize = Tokens.Fonts.Small;
        box.IsVisible = false;
        _icon = MoreMenu.HeldIcon(Icons.Search);
        ToolTip.SetTip(_icon, "Search (Ctrl+F)");
        Avalonia.Automation.AutomationProperties.SetName(_icon, "Search");
        _icon.Click += (_, _) => Open();

        box.LostFocus += (_, _) => { if (string.IsNullOrEmpty(box.Text)) Close(); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            box.Text = string.Empty;
            Close();
            e.Handled = true;
        };

        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_icon);
        Children.Add(box);
    }

    /// <summary>The search field.</summary>
    public TextBox Box { get; }

    /// <summary>Whether the field is showing.</summary>
    public bool IsOpen => Box.IsVisible;

    /// <summary>Opens the field and puts the caret in it.</summary>
    public void Open()
    {
        _icon.IsVisible = false;
        Box.IsVisible = true;
        Box.Focus();
        Box.SelectAll();
    }

    private void Close()
    {
        Box.IsVisible = false;
        _icon.IsVisible = true;
    }
}

/// <summary>
/// The "..." on the title row: the rare and the destructive (open the file by hand, clear the
/// history), behind one quiet button so they never take the page's room.
/// </summary>
public static class MoreMenu
{
    /// <summary>Builds the button; <paramref name="items"/> sit in its flyout, one under another.</summary>
    public static SgButton Make(params Control[] items)
    {
        var button = HeldIcon(Icons.More);
        ToolTip.SetTip(button, "More");
        Avalonia.Automation.AutomationProperties.SetName(button, "More");
        var column = new StackPanel { Spacing = Tokens.Space.Hair };
        foreach (var item in items)
        {
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
            column.Children.Add(item);
        }
        button.Flyout = new Flyout { Content = column, Placement = PlacementMode.BottomEdgeAlignedRight };
        return button;
    }

    /// <summary>An icon button sized to sit inside a bed, as a segment does.</summary>
    public static SgButton HeldIcon(string icon) => new(string.Empty, SgButton.Kind.Quiet, compact: true, icon: icon)
    {
        Width = Tokens.Layout.SegmentHeight + Tokens.Space.Tight,
        Height = Tokens.Layout.SegmentHeight,
        Padding = new Thickness(0),
    };

    /// <summary>
    /// Holds icon buttons on the house bed, the tabs' shape, so the title row's icons sit in a
    /// held control rather than floating beside the title.
    /// </summary>
    public static Border Held(params Control[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Hair };
        foreach (var button in buttons) row.Children.Add(button);
        return new Border
        {
            Background = Tokens.Brushes.ToggleBed,
            CornerRadius = new CornerRadius(Tokens.Radius.Button),
            Padding = new Thickness(Tokens.Space.TrackInset),
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
        };
    }
}
