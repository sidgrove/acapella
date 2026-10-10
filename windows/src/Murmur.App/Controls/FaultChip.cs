using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Design;

namespace Murmur.App.Controls;

/// <summary>
/// A fault as one small chip on the line it is about: a warning mark, the start of the sentence
/// and a cross. The whole sentence is its tooltip and its accessible name; pressing it clears it.
/// </summary>
/// <remarks>
/// The Bible: "A warning is a small chip on the line it is about, never a full-width bar." It was
/// a coral bar the width of the status card with a loose "Dismiss" beside it (audit of
/// 10/10/2026). Coral is the house hue for something that failed; red stays with recording.
/// </remarks>
public sealed class FaultChip : Button
{
    private readonly TextBlock _words;
    private readonly Glyph _close;
    private bool _bare;

    /// <summary>Creates the chip, hidden until there is something to say.</summary>
    public FaultChip()
    {
        var accent = Tokens.Accent.Coral;
        _words = new TextBlock
        {
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Badge,
            FontWeight = FontWeight.SemiBold,
            Foreground = accent.Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(Tokens.Space.Tight, 0),
        };
        var mark = new Glyph(Icons.Alert, Tokens.Fonts.Badge, accent.Ink);
        DockPanel.SetDock(mark, Dock.Left);
        _close = new Glyph(Icons.Close, Tokens.Fonts.Badge, accent.Ink);
        DockPanel.SetDock(_close, Dock.Right);

        var skin = new Border
        {
            Height = Tokens.Layout.ChipHeight,
            MaxWidth = Tokens.Layout.FaultChipWidth,
            CornerRadius = new CornerRadius(Tokens.Radius.Button),
            Background = accent.Fill,
            Padding = new Thickness(Tokens.Space.Chip, 0),
            Child = new DockPanel { Children = { mark, _close, _words } },
        };

        Background = Tokens.Brushes.None;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
        FocusAdorner = FocusHalo.For(Tokens.Radius.Button);
        Template = new FuncControlTemplate<FaultChip>((_, _) => skin);
        IsVisible = false;
    }

    /// <summary>The fault, as a whole sentence.</summary>
    public string Message
    {
        get => _words.Text ?? string.Empty;
        set
        {
            _words.Text = value;
            ToolTip.SetTip(this, value);
            Avalonia.Automation.AutomationProperties.SetName(this, $"{value} Press to clear.");
        }
    }

    /// <summary>The mark alone, where there is no room for words; the sentence stays in the tooltip.</summary>
    public bool IsBare
    {
        get => _bare;
        set
        {
            if (_bare == value) return;
            _bare = value;
            _words.IsVisible = !value;
            _close.IsVisible = !value;
        }
    }
}
