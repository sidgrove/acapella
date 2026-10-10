using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Design;

namespace Murmur.App.Controls;

/// <summary>The key focus halo: the soft brand ring every control wears when it is reached by Tab.</summary>
/// <remarks>
/// The app had no focus style of its own, so Tab drew the theme's hard black box (audit of
/// 10/10/2026). Sidgrove Intelligence paints one halo on everything, <c>0 0 0 2px
/// rgba(104,116,180,0.28)</c>, flush; this is that, drawn just outside the control it rings.
/// </remarks>
public static class FocusHalo
{
    /// <summary>The halo for a control whose corner is <paramref name="radius"/>.</summary>
    public static ITemplate<Control> For(double radius) => new FuncTemplate<Control>(() => new Border
    {
        BorderBrush = Tokens.Brushes.FocusHalo,
        BorderThickness = new Thickness(Tokens.Border.FocusHalo),
        CornerRadius = new CornerRadius(radius + Tokens.Border.FocusHalo),
        Margin = new Thickness(-Tokens.Border.FocusHalo),
        IsHitTestVisible = false,
    });
}

/// <summary>
/// One section on the title row: a soft square of its own, led by the section's small tinted
/// mark, the current one white, lifted and a weight bolder. A count rides beside the name.
/// </summary>
/// <remarks>
/// This is Sidgrove Intelligence's control for choosing the current section
/// (<c>SegmentedPicker appearance="buttons"</c>, the Bible, Part 2 §5), which Dave picked on the
/// web app's joy review of 10/10/2026: "more colour, the icons and softer". Until then the
/// sections sat on the toggle's bed with a gliding thumb, the same control as Instant / Polished,
/// doing a different job. The hue lives on the mark and nowhere else; selection is elevation and
/// weight, never hue.
/// </remarks>
public sealed class NavLink : Button
{
    private readonly Chip _count;
    private readonly Border _skin;
    private readonly TextBlock _label;
    private readonly TextBlock _ghost;
    private readonly Panel _words;
    private readonly IconTile? _mark;
    private bool _active;
    private bool _compact;
    private bool _namedByTip;
    private bool _inRow;

    /// <summary>Creates the section, optionally led by <paramref name="icon"/> on a tile in <paramref name="accent"/>'s hue.</summary>
    public NavLink(string text, string? icon = null, Tokens.Accent? accent = null)
    {
        _label = Words(text, FontWeight.Medium);
        // The word sits over a hidden copy of itself at the chosen weight, so becoming the
        // current section cannot widen the button and nudge its neighbours.
        _ghost = Words(text, FontWeight.SemiBold);
        _ghost.Opacity = 0;
        _words = new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { _ghost, _label } };
        _count = new Chip(string.Empty, Tokens.Accent.Amber) { IsVisible = false, Height = Tokens.Layout.CountChipHeight };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Snug, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
        {
            _mark = new IconTile(icon, accent ?? Tokens.Accent.MarkBrand, Tokens.Layout.TileSmall);
            row.Children.Add(_mark);
        }
        row.Children.Add(_words);
        row.Children.Add(_count);

        _skin = new Border
        {
            Height = Tokens.Layout.ButtonHeightSmall,
            CornerRadius = new CornerRadius(Tokens.Radius.Segment),
            Child = row,
        };
        if (Tokens.Motion.Animate) _skin.Transitions = [new BrushTransition { Property = Border.BackgroundProperty, Duration = Tokens.Motion.Quick }];

        Background = Tokens.Brushes.None;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        ClipToBounds = false;
        Cursor = new Cursor(StandardCursorType.Hand);
        FocusAdorner = FocusHalo.For(Tokens.Radius.Segment);
        Template = new FuncControlTemplate<NavLink>((_, _) => _skin);
        Avalonia.Automation.AutomationProperties.SetName(this, text);
        Shape();
        Paint();
    }

    /// <summary>The section's name.</summary>
    public string Text
    {
        get => _label.Text ?? string.Empty;
        set { _label.Text = value; _ghost.Text = value; }
    }

    /// <summary>Something waiting in the section, shown as a chip beside the name; nothing at zero.</summary>
    public int Count
    {
        get => int.TryParse(_count.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        set
        {
            _count.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _count.IsVisible = value > 0;
        }
    }

    /// <summary>
    /// The sections side by side, each its own button with a little air between: no bed and no
    /// thumb. One tab stop, and the arrow keys move along the row and choose, as the web control's do.
    /// </summary>
    public static Border Track(params NavLink[] links)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Chip };
        for (var i = 0; i < links.Length; i++)
        {
            var index = i;
            links[i].KeyDown += (_, e) =>
            {
                var next = e.Key == Key.Right ? index + 1 : e.Key == Key.Left ? index - 1 : -1;
                if (next < 0 || next >= links.Length) return;
                e.Handled = true;
                links[next].Focus(NavigationMethod.Directional);
                links[next].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(ClickEvent));
            };
            links[i]._inRow = true;
            links[i].IsTabStop = links[i]._active;
            row.Children.Add(links[i]);
        }
        return new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            ClipToBounds = false,
            Child = row,
        };
    }

    /// <summary>Whether this is the current section.</summary>
    public bool IsActive
    {
        get => _active;
        set { _active = value; if (_inRow) IsTabStop = value; Shape(); Paint(); }
    }

    /// <summary>
    /// In a window too narrow for every section's name, the sections that are not current keep
    /// their mark and give up their word (it moves to the tooltip), so nothing on the title row
    /// is cut off at the edge. The Bible: "Nothing clips."
    /// </summary>
    public bool IsCompact
    {
        get => _compact;
        set
        {
            if (_compact == value) return;
            _compact = value;
            // A mark alone needs its name somewhere; a link that already explains itself keeps its own
            // tip, and the name goes again with the room back (a tip never repeats the word beside it).
            if (value && ToolTip.GetTip(this) is null)
            {
                ToolTip.SetTip(this, Text);
                _namedByTip = true;
            }
            else if (!value && _namedByTip)
            {
                ToolTip.SetTip(this, null);
                _namedByTip = false;
            }
            Shape();
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty) Paint();
    }

    private void Shape()
    {
        // A mark with no word is a square; with its word, the mark sits a little in from the left
        // and the word has the button's own air after it.
        var bare = _compact && !_active && _mark is not null;
        _words.IsVisible = !bare;
        _label.IsVisible = !bare;
        var lead = _mark is null ? Tokens.Layout.ButtonPadXSmall : Tokens.Layout.SectionMarkInset;
        _skin.Padding = bare ? new Thickness(lead, 0) : new Thickness(lead, 0, Tokens.Layout.ButtonPadXSmall, 0);
    }

    private void Paint()
    {
        var over = IsPointerOver && !_active;
        _skin.Background = _active || over ? Tokens.Brushes.Card : Tokens.Brushes.SectionRest;
        _skin.BoxShadow = _active ? Tokens.Shadow.Section : Tokens.Shadow.None;
        _label.Foreground = _active ? Tokens.Brushes.InkSite : Tokens.Brushes.Muted;
        _label.FontWeight = _active ? FontWeight.SemiBold : FontWeight.Medium;
        if (_mark is not null) _mark.Opacity = _active || over ? 1 : Tokens.Opacity.MarkResting;
        // Under the pointer an unchosen section lifts a pixel towards being chosen; pressed, it settles.
        RenderTransform = new TranslateTransform(0, over && !IsPressed ? -Tokens.Motion.PressTravel : 0);
    }

    private static TextBlock Words(string text, FontWeight weight) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Sans,
        FontSize = Tokens.Fonts.Small,
        FontWeight = weight,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };
}

/// <summary>
/// A row that opens its settings in place: the whole line is the button (the section's tile, its
/// name and what it is set to in plain words), a quiet chevron at its end that turns as it opens,
/// and the panel beneath. <c>DisclosureRow.tsx</c> in Sidgrove Intelligence.
/// </summary>
/// <remarks>
/// Dave, 03/10/2026, of the web app: "why can't I click the row to drop down". Settings was
/// eleven open cards and some forty question marks in one scroll (audit of 10/10/2026); folded,
/// it is eleven lines that can be read, and the explanation waits beside the name of the row
/// that is open.
/// </remarks>
public sealed class DisclosureRow : Border
{
    private readonly TextBlock _summary;
    private readonly Glyph _chevron;
    private readonly Border _panel;
    private readonly Border? _hint;
    private readonly Border _skin;
    private readonly Button _head;
    private bool _open;

    /// <summary>Raised when the row opens or closes.</summary>
    public event EventHandler? Toggled;

    /// <summary>Creates the row, closed.</summary>
    /// <param name="icon">One of <see cref="Icons"/>.</param>
    /// <param name="accent">The section's own hue, on its tile.</param>
    /// <param name="title">The section's name.</param>
    /// <param name="tip">What the section is for; shown as a hint beside the name while the row is open.</param>
    /// <param name="body">The settings themselves.</param>
    /// <param name="first">The first row of its list draws no line above itself.</param>
    public DisclosureRow(string icon, Tokens.Accent accent, string title, string? tip, Control body, bool first = false)
    {
        Title = title;
        var name = Text.Body(title);
        name.FontWeight = FontWeight.SemiBold;
        name.TextWrapping = TextWrapping.NoWrap;
        name.VerticalAlignment = VerticalAlignment.Center;

        _summary = Text.Muted(string.Empty);
        _summary.TextWrapping = TextWrapping.NoWrap;
        _summary.TextTrimming = TextTrimming.CharacterEllipsis;
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _summary.Margin = new Thickness(Tokens.Space.Base, 0);

        _chevron = new Glyph(Icons.ChevronRight, Tokens.Layout.Chevron, Tokens.Brushes.Faint)
        {
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("rotate(0deg)"),
        };
        if (Tokens.Motion.Animate) _chevron.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = Tokens.Motion.Quick }];
        DockPanel.SetDock(_chevron, Dock.Right);

        var lead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Base, VerticalAlignment = VerticalAlignment.Center };
        lead.Children.Add(new IconTile(icon, accent));
        lead.Children.Add(name);
        if (tip is not null)
        {
            _hint = Hint.Make(tip);
            _hint.IsVisible = false;
            lead.Children.Add(_hint);
        }
        DockPanel.SetDock(lead, Dock.Left);

        // The row's hover wash reaches a little past its words either side, as the web row's does.
        _skin = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Control),
            Padding = new Thickness(Tokens.Space.Snug, Tokens.Layout.DisclosurePadY),
            Margin = new Thickness(-Tokens.Space.Snug, 0),
            Background = Tokens.Brushes.None,
            Child = new DockPanel { Children = { _chevron, lead, _summary } },
        };
        if (Tokens.Motion.Animate) _skin.Transitions = [new BrushTransition { Property = BackgroundProperty, Duration = Tokens.Motion.Quick }];

        _head = new Button
        {
            Background = Tokens.Brushes.None,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ClipToBounds = false,
            Cursor = new Cursor(StandardCursorType.Hand),
            FocusAdorner = FocusHalo.For(Tokens.Radius.Control),
            Template = new FuncControlTemplate<Button>((_, _) => _skin),
        };
        _head.Click += (_, _) => IsOpen = !IsOpen;
        _head.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsPointerOverProperty) _skin.Background = _head.IsPointerOver ? Tokens.Brushes.Surface : Tokens.Brushes.None;
        };

        _panel = new Border
        {
            Padding = new Thickness(0, Tokens.Space.Snug, 0, Tokens.Space.Wide),
            IsVisible = false,
            Child = body,
        };
        if (Tokens.Motion.Animate) _panel.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = Tokens.Motion.Quick }];

        BorderBrush = Tokens.Brushes.RowLine;
        BorderThickness = new Thickness(0, first ? 0 : Tokens.Border.Hairline, 0, 0);
        Child = new StackPanel { Children = { _head, _panel } };
        Announce();
    }

    /// <summary>The section's name.</summary>
    public string Title { get; }

    /// <summary>What the section is set to, in plain words.</summary>
    public string Summary => _summary.Text ?? string.Empty;

    /// <summary>Whether the summary is reporting a problem: something this section needs and does not have.</summary>
    public bool HasProblem { get; private set; }

    /// <summary>
    /// Says what the section is set to. A problem is said in coral, the house hue for something
    /// that failed or is missing; never red, which is recording's alone.
    /// </summary>
    public void Say(string summary, bool problem = false)
    {
        _summary.Text = summary;
        _summary.Foreground = problem ? Tokens.Accent.Coral.Ink : Tokens.Brushes.Muted;
        HasProblem = problem;
        Announce();
    }

    /// <summary>Whether the row's settings are showing.</summary>
    public bool IsOpen
    {
        get => _open;
        set
        {
            if (_open == value) return;
            _open = value;
            _panel.Opacity = value && Tokens.Motion.Animate ? 0 : 1;
            _panel.IsVisible = value;
            if (value && Tokens.Motion.Animate) _panel.Opacity = 1;
            if (_hint is not null) _hint.IsVisible = value;
            _chevron.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(value ? "rotate(90deg)" : "rotate(0deg)");
            Announce();
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Opens or closes the row as a click would. Exposed for headless tests.</summary>
    public void Press() => IsOpen = !IsOpen;

    private void Announce() =>
        Avalonia.Automation.AutomationProperties.SetName(_head, $"{Title}: {Summary}. {(_open ? "Open" : "Closed")}");
}
