using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.App.Views;

namespace Murmur.AppTests.BibleAudit;

/// <summary>The real Settings page, found in the shown window so an option can stand in its place.</summary>
internal static class SettingsPage
{
    /// <summary>Opens Settings and returns the column its cards stand in.</summary>
    public static StackPanel Open(MainWindow window)
    {
        window.ShowSettings();
        window.UpdateLayout();
        var view = window.GetVisualDescendants().OfType<SettingsView>().Single();
        return (StackPanel)((ScrollViewer)view.Content!).Content!;
    }

    /// <summary>Hides today's cards from <paramref name="from"/> on. Nothing is removed.</summary>
    public static void Hide(StackPanel body, int from = 0)
    {
        for (var i = from; i < body.Children.Count; i++) body.Children[i].IsVisible = false;
    }

    /// <summary>A setting that is on or off, as the page draws one: its words, then the switch at the row's end.</summary>
    public static DockPanel Switched(string label, bool on)
    {
        var toggle = new Switch { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(toggle, Dock.Right);
        var name = Text.Body(label);
        name.VerticalAlignment = VerticalAlignment.Center;
        return new DockPanel { Children = { toggle, name } };
    }
}

/// <summary>
/// A, "Folded rows": every section is one row that says what it is set to in plain words and opens
/// in place (the web app's <c>DisclosureRow</c>: the whole row is the button, a quiet chevron at
/// its end). Eleven open cards and thirty-three settings become eleven calm lines on one screen.
/// </summary>
internal static class SettingsA
{
    public const string Name = "A  Folded rows";
    public const string Line = "Each section is one line that says what it is set to, and opens in place. The whole page on one screen.";

    public static void Place(StackPanel body)
    {
        SettingsPage.Hide(body);
        var rows = new StackPanel();
        Add(rows, Icons.Keyboard, Tokens.Accent.Brand, "Push to talk", "Right Ctrl, hold or tap");
        Add(rows, Icons.Mic, Tokens.Accent.Info, "Microphone", "Windows default");
        Add(rows, Icons.Volume, Tokens.Accent.Amber, "Sounds", "Recording on, send on", open: Panels(
            SettingsPage.Switched("Recording sounds", true),
            SettingsPage.Switched("Send sound", true)));
        Add(rows, Icons.Cpu, Tokens.Accent.Slate, "Hearing you", "On this PC");
        Add(rows, Icons.Pen, Tokens.Accent.Emerald, "Writing", "British spellings, no ums, full stop dropped after one sentence");
        Add(rows, Icons.Send, Tokens.Accent.Coral, "Sending", "Say blob or send it");
        Add(rows, Icons.Sparkles, Tokens.Accent.Plum, "AI clean-up", "Polished, with Gemini 2.5 Flash");
        Add(rows, Icons.Learn, Tokens.Accent.Brand, "Learning from you", "On, asks before adding a word");
        Add(rows, Icons.Zap, Tokens.Accent.Info, "Jev decisions", "Off");
        Add(rows, Icons.Cloud, Tokens.Accent.Emerald, "Sync", "Not signed in");
        Add(rows, Icons.Toggle, Tokens.Accent.Slate, "Behaviour", "Types into your app, keeps a history");
        var card = Card.Standard(rows);
        card.Padding = new Thickness(Tokens.Space.Card, Tokens.Space.Chip);
        body.Children.Insert(0, card);
    }

    private static StackPanel Panels(params Control[] children)
    {
        var panel = new StackPanel { Spacing = Tokens.Space.Base, Margin = new Thickness(Tokens.Layout.Tile + Tokens.Space.Base, 0, 0, Tokens.Space.Roomy) };
        panel.Children.AddRange(children);
        return panel;
    }

    private static void Add(StackPanel rows, string icon, Tokens.Accent accent, string name, string summary, Control? open = null)
    {
        var label = Text.Body(name);
        label.FontWeight = FontWeight.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        var says = Text.Muted(summary);
        says.VerticalAlignment = VerticalAlignment.Center;
        says.TextWrapping = TextWrapping.NoWrap;
        says.TextTrimming = TextTrimming.CharacterEllipsis;
        // DisclosureRow.tsx: a 16px chevron in #8a8fa8 that turns as the row opens.
        var chevron = new Glyph(open is null ? House.ChevronRight : House.ChevronDown, 16, House.Hex("#8a8fa8"));
        DockPanel.SetDock(chevron, Dock.Right);
        var tile = new IconTile(icon, accent) { Margin = new Thickness(0, 0, Tokens.Space.Base, 0) };
        DockPanel.SetDock(tile, Dock.Left);
        label.Margin = new Thickness(0, 0, Tokens.Space.Base, 0);
        DockPanel.SetDock(label, Dock.Left);
        var head = new DockPanel { Margin = new Thickness(0, 10), Children = { chevron, tile, label, says } };
        var column = new StackPanel { Children = { head } };
        if (open is not null) column.Children.Add(open);
        rows.Children.Add(new Border
        {
            // DisclosureRow.tsx: border-t #eef0f6, none on the first.
            BorderBrush = House.Hex("#eef0f6"),
            BorderThickness = new Thickness(0, rows.Children.Count == 0 ? 0 : 1, 0, 0),
            Child = column,
        });
    }
}

/// <summary>
/// B, "Everyday first": the page is the four things changed on most visits (the key, the
/// microphone, the writing mode and the sounds) in one card. Everything else is there, one click
/// away, behind a single soft pill that says how many (the web app's <c>MorePill</c>).
/// </summary>
internal static class SettingsB
{
    public const string Name = "B  Everyday first";
    public const string Line = "One card of the four things changed most. The other 29 wait behind one soft pill.";

    public static void Place(StackPanel body)
    {
        SettingsPage.Hide(body);

        var change = new SgButton("Change", SgButton.Kind.Ghost, compact: true);
        var key = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Base, Children = { KeyCaps.Make("Right Ctrl"), change } };
        var mode = new Segmented(
        [
            new Segmented.Choice("Instant", Icons.Zap, Tokens.Accent.Amber),
            new Segmented.Choice("Polished", Icons.Sparkles, Tokens.Accent.Plum),
        ], selected: 1);
        var rows = new StackPanel { Spacing = Tokens.Space.Roomy };
        rows.Children.Add(Field("Shortcut", key));
        rows.Children.Add(Field("Microphone", Picker("Windows default")));
        rows.Children.Add(Field("Writing mode", mode));
        rows.Children.Add(Field("Sounds", new Switch { IsChecked = true }));
        body.Children.Insert(0, Card.Standard(rows));
        body.Children.Insert(1, MorePill("29", "more settings"));
    }

    /// <summary>A form row as the page draws one: the label in its fixed column, the control beside it, only as wide as what it holds.</summary>
    private static Grid Field(string label, Control control)
    {
        var name = Text.Body(label);
        name.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        return new Grid { ColumnDefinitions = new ColumnDefinitions($"{Tokens.Layout.LabelColumn},*"), MinHeight = Tokens.Layout.FieldHeight, Children = { name, control } };
    }

    /// <summary>A picker at rest: a soft box and a faint chevron, about as wide as a device's name.</summary>
    private static Border Picker(string value)
    {
        var chevron = new Glyph(House.ChevronDown, 14, Tokens.Brushes.Faint);
        DockPanel.SetDock(chevron, Dock.Right);
        var words = Text.Body(value);
        words.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            // As wide as a device's name and no wider; 240 still fits the smallest window's column.
            Width = 240,
            Height = Tokens.Layout.FieldHeight,
            CornerRadius = new CornerRadius(Tokens.Radius.Control),
            Background = Tokens.Brushes.Card,
            BorderBrush = Tokens.Brushes.PanelBorder,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Layout.FieldPadX, 0),
            Child = new DockPanel { Children = { chevron, words } },
        };
    }

    /// <summary>MorePill.tsx: 28px, fully round, on the #eef0f7 bed, the number bold, a chevron on a white disc.</summary>
    private static Border MorePill(string count, string words)
    {
        var number = new TextBlock { Text = count, FontFamily = Tokens.Fonts.Sans, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Tokens.Brushes.Ink, VerticalAlignment = VerticalAlignment.Center };
        var rest = new TextBlock { Text = words, FontFamily = Tokens.Fonts.Sans, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = Tokens.Brushes.Muted, VerticalAlignment = VerticalAlignment.Center };
        var disc = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Tokens.Brushes.Card,
            Margin = new Thickness(Tokens.Space.Tight, 0, 0, 0),
            Child = new Glyph(House.ChevronDown, 12, Tokens.Brushes.Muted) { HorizontalAlignment = HorizontalAlignment.Center },
        };
        return new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = Tokens.Brushes.ToggleBed,
            Padding = new Thickness(Tokens.Space.Base, 0, Tokens.Space.Tight, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Tight, Children = { number, rest, disc } },
        };
    }
}

/// <summary>
/// C, "Four groups on one strip": today's cards, untouched, sorted into four groups by what they
/// are about, with the house toggle choosing the group. Each group is two or three cards and fits
/// a screen, so the page never becomes a long scroll.
/// </summary>
internal static class SettingsC
{
    public const string Name = "C  Four groups on one strip";
    public const string Line = "Today's cards, sorted into four groups. One toggle picks the group; each fits a screen.";

    public static void Place(StackPanel body)
    {
        // Talking is the first three of today's cards: the key, the microphone and the sounds.
        SettingsPage.Hide(body, from: 3);
        body.Children.Insert(0, new Segmented(["Talking", "Writing", "Clean-up and learning", "This PC"]));
    }
}
