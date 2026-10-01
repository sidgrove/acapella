using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Murmur.App.Controls;
using Murmur.App.Design;

namespace Murmur.App.Views;

/// <summary>Shared layout helpers. Every value comes from <see cref="Tokens"/>.</summary>
internal static class Panels
{
    /// <summary>Docks a control and returns it, so it reads inline in a Children list.</summary>
    public static Control Docked(Control control, Dock side)
    {
        DockPanel.SetDock(control, side);
        return control;
    }

    /// <summary>A label above a control, for a dialog where the form is narrow.</summary>
    public static StackPanel Labelled(string label, Control content) => new()
    {
        Spacing = Tokens.Space.Chip,
        Children = { Text.Label(label), content },
    };

    /// <summary>A row with content on the left and actions on the right.</summary>
    public static DockPanel Split(Control leading, Control trailing)
    {
        DockPanel.SetDock(trailing, Dock.Right);
        trailing.VerticalAlignment = VerticalAlignment.Center;
        leading.VerticalAlignment = VerticalAlignment.Center;
        return new DockPanel { Children = { trailing, leading } };
    }

    /// <summary>A horizontal run of controls.</summary>
    public static StackPanel Row(double spacing, params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    /// <summary>A vertical run of controls.</summary>
    public static StackPanel Column(double spacing, params Control[] children)
    {
        var column = new StackPanel { Spacing = spacing };
        foreach (var child in children) column.Children.Add(child);
        return column;
    }

    /// <summary>
    /// A card with its header: the section's tinted tile, its name and a hint carrying what it
    /// is for, then the body. Never a description sentence under the title.
    /// </summary>
    public static Border SettingsCard(string icon, Tokens.Accent accent, string title, string? tip, Control body)
    {
        var card = Card.Standard(Column(Tokens.Space.Wide, SectionHead.Make(icon, accent, title, tip), body));
        return card;
    }

    /// <summary>
    /// A setting that is on or off: the label, a hint with the detail for anyone who wants it,
    /// and the switch on the right.
    /// </summary>
    public static DockPanel SwitchRow(string label, string? helper, bool value, Action<bool> onChange)
    {
        var toggle = new Switch { IsChecked = value };
        toggle.IsCheckedChanged += (_, _) => onChange(toggle.IsChecked == true);

        var name = Text.Body(label);
        name.VerticalAlignment = VerticalAlignment.Center;
        var text = Row(Tokens.Space.Chip, name);
        if (helper is not null) text.Children.Add(Hint.Make(helper));
        text.Margin = new Thickness(0, 0, Tokens.Space.Wide, 0);

        return Split(text, toggle);
    }

    /// <summary>
    /// A field in a form: the label in a fixed column with its control beside it, the control
    /// only as wide as what it holds.
    /// </summary>
    public static Grid FieldRow(string label, string? tip, Control control)
    {
        var name = Text.Body(label);
        name.VerticalAlignment = VerticalAlignment.Center;
        var left = Row(Tokens.Space.Chip, name);
        if (tip is not null) left.Children.Add(Hint.Make(tip));
        left.VerticalAlignment = control is TextBox { AcceptsReturn: true } ? VerticalAlignment.Top : VerticalAlignment.Center;
        left.Margin = new Thickness(0, 0, Tokens.Space.Roomy, 0);

        control.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Tokens.Layout.LabelColumn},*") };
        Grid.SetColumn(control, 1);
        grid.Children.Add(left);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>
    /// The empty state: a small tile and one quiet line. No onboarding headline, no large icon
    /// (the Bible, Part 2 §4).
    /// </summary>
    public static Control EmptyState(string icon, Tokens.Accent accent, string line)
    {
        var words = Text.Muted(line);
        words.VerticalAlignment = VerticalAlignment.Center;
        var row = Row(Tokens.Space.Base, new IconTile(icon, accent), words);
        row.HorizontalAlignment = HorizontalAlignment.Center;

        var card = Card.Standard(row);
        card.Padding = new Thickness(Tokens.Space.Wide, Tokens.Space.Section);
        return card;
    }

    /// <summary>Constrains content to the reading column and centres it.</summary>
    public static Border Column(Control content) => new()
    {
        MaxWidth = Tokens.Layout.ContentMaxWidth,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = content,
    };
}
