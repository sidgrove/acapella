using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Murmur.App.Controls;
using Murmur.App.Design;

namespace Murmur.AppTests.BibleAudit;

/// <summary>
/// The values the options are drawn with, each read from the Sidgrove Intelligence web app's own
/// source on 10/10/2026 and named beside the file it came from. They live here, in the test
/// project, and nowhere in the shipping app: the options are drawings for Dave to choose from
/// (the Bible, "Options before a rebuild"), and only the one he picks is lifted into the app.
/// </summary>
internal static class House
{
    /// <summary>A solid brush from a hex.</summary>
    public static IBrush Hex(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>A section's hue as a tile accent. <c>lib/brand/module-identity.ts</c>, <c>MODULE_HUES</c>.</summary>
    public static Tokens.Accent Hue(string ink, string fill) => new(Hex(fill), Hex(fill), Hex(ink));

    /// <summary><c>MODULE_HUES.brand</c>.</summary>
    public static Tokens.Accent Brand { get; } = Hue("#3d4785", "#e4e6f2");

    /// <summary><c>MODULE_HUES.green</c>.</summary>
    public static Tokens.Accent Green { get; } = Hue("#3f7d4d", "#e0ece1");

    /// <summary><c>MODULE_HUES.purple</c>.</summary>
    public static Tokens.Accent Purple { get; } = Hue("#7c5dab", "#ede7f6");

    /// <summary>The app-wide key focus halo. <c>app/globals.css</c>: <c>0 0 0 2px rgba(104,116,180,0.28)</c>.</summary>
    public static BoxShadow FocusHalo { get; } = new() { Spread = 2, Color = Color.FromArgb(71, 104, 116, 180) };

    /// <summary><c>PILL_NAV_ACTIVE_LIFT</c>, <c>components/ui/PillNav.tsx</c>: one keyline and one contact shadow.</summary>
    public static BoxShadows ActiveLift { get; } = new(
        new BoxShadow { Spread = 1, Color = Color.FromArgb(10, 15, 23, 42) },
        [new BoxShadow { OffsetY = 1, Blur = 2, Color = Color.FromArgb(10, 15, 23, 42) }]);

    /// <summary>The chosen section button's shadow. <c>SECTION_BUTTON_ON</c>: <c>0 1px 2px rgba(15,23,42,0.06)</c>.</summary>
    public static BoxShadows SectionLift { get; } = new(new BoxShadow { OffsetY = 1, Blur = 2, Color = Color.FromArgb(15, 15, 23, 42) });

    /// <summary>A chevron pointing right, on the icons' 24-unit grid (Lucide).</summary>
    public const string ChevronRight = "M9 18l6-6-6-6";

    /// <summary>A chevron pointing down.</summary>
    public const string ChevronDown = "M6 9l6 6 6-6";

    /// <summary>
    /// The section buttons. <c>components/ui-ext/SegmentedPicker.tsx</c>, <c>appearance="buttons"</c>,
    /// size sm: 32px, 7px corner, fill only, a 22px mark 5px in, 12px words, white and lifted when chosen.
    /// </summary>
    public static Look SectionButton { get; } = new(
        Height: 32, Radius: 7, Padding: new Thickness(5, 0, 12, 0), Gap: 8, FontSize: 12,
        RestFill: Hex("#f6f7fb"), RestEdge: Brushes.Transparent, RestInk: Hex("#525672"), RestWeight: FontWeight.Medium,
        HoverFill: Brushes.White, HoverEdge: Brushes.Transparent, HoverInk: Hex("#525672"), HoverLift: 1,
        OnFill: Brushes.White, OnEdge: Brushes.Transparent, OnInk: Hex("#1a1d2e"), OnWeight: FontWeight.SemiBold, OnShadow: SectionLift,
        TileSize: 22, TileRestOpacity: 0.8);

    /// <summary>
    /// A sibling page on the quiet strip, wearing its identity tile. <c>components/ui/PillNav.tsx</c>,
    /// <c>variant="quiet"</c> with a <c>tile</c>: 36px, 7px corner, a hairline edge, a 26px tile 4px
    /// in, the current page white, lifted and bold.
    /// </summary>
    public static Look QuietPage { get; } = new(
        Height: 36, Radius: 7, Padding: new Thickness(4, 0, 14, 0), Gap: 10, FontSize: 12,
        RestFill: Hex("#f7f8fc"), RestEdge: Hex("#edeff5"), RestInk: Hex("#4a4f6a"), RestWeight: FontWeight.Medium,
        HoverFill: Hex("#eef0fa"), HoverEdge: Hex("#dfe1ee"), HoverInk: Hex("#3d4785"), HoverLift: 0,
        OnFill: Brushes.White, OnEdge: Brushes.Transparent, OnInk: Hex("#3d4785"), OnWeight: FontWeight.Bold, OnShadow: ActiveLift,
        TileSize: 26, TileRestOpacity: 1);

    /// <summary>
    /// A title-row action that leads with its own mark. <c>HEADER_ACTION_MARKED</c>,
    /// <c>docs/UI-CONTRACT.md</c>: 32px, 10px corner, white, edge <c>#e3e6ef</c>, a 22px tile 5px in.
    /// </summary>
    public static Look MarkedAction { get; } = new(
        Height: 32, Radius: 10, Padding: new Thickness(5, 0, 12, 0), Gap: 8, FontSize: 12,
        RestFill: Brushes.White, RestEdge: Hex("#e3e6ef"), RestInk: Hex("#3a4159"), RestWeight: FontWeight.SemiBold,
        HoverFill: Brushes.White, HoverEdge: Hex("#cbd1de"), HoverInk: Hex("#3a4159"), HoverLift: 1,
        OnFill: Brushes.White, OnEdge: Hex("#cbd1de"), OnInk: Hex("#1a1d2e"), OnWeight: FontWeight.SemiBold, OnShadow: SectionLift,
        TileSize: 22, TileRestOpacity: 1);
}

/// <summary>How one held button looks at rest, under the pointer and when it is the current one.</summary>
internal sealed record Look(
    double Height, double Radius, Thickness Padding, double Gap, double FontSize,
    IBrush RestFill, IBrush RestEdge, IBrush RestInk, FontWeight RestWeight,
    IBrush HoverFill, IBrush HoverEdge, IBrush HoverInk, double HoverLift,
    IBrush OnFill, IBrush OnEdge, IBrush OnInk, FontWeight OnWeight, BoxShadows OnShadow,
    double TileSize, double TileRestOpacity);

/// <summary>
/// One section as a real control: a mark in the section's own hue, its name, and a count when
/// something waits. It answers the pointer, the press and the keys, and draws the house key
/// focus halo itself, so each state on the sheets is the control's own and not a drawing of it.
/// </summary>
internal sealed class HeldButton : Button
{
    private readonly Look _look;
    private readonly Border _skin;
    private readonly TextBlock _label;
    private readonly Panel _words;
    private readonly IconTile _tile;
    private readonly Control? _trailing;
    private readonly Border? _corner;
    private bool _active;
    private bool _keyFocus;
    private bool _bare;

    /// <summary>Creates the button.</summary>
    /// <param name="name">Its word, and its accessible name.</param>
    /// <param name="icon">One of <see cref="Icons"/>, drawn on a tile.</param>
    /// <param name="hue">The tile's hue. The hue lives on the tile and nowhere else.</param>
    /// <param name="look">The states.</param>
    /// <param name="waiting">Something waiting in the section: a small amber count, nothing at zero.</param>
    /// <param name="onCorner">Whether the count rides the tile's corner (a disc) or follows the word (a chip).</param>
    public HeldButton(string name, string icon, Tokens.Accent hue, Look look, int waiting = 0, bool onCorner = false)
    {
        _look = look;
        Word = name;
        _label = Words(name, look.FontSize, look.RestWeight);
        // The word sits over a hidden copy of itself at the chosen weight, so going bold cannot
        // widen the box (the web control does the same with a zero-height bold copy).
        var ghost = Words(name, look.FontSize, look.OnWeight);
        ghost.Opacity = 0;
        _words = new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { ghost, _label } };

        _tile = new IconTile(icon, hue, look.TileSize);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = look.Gap, VerticalAlignment = VerticalAlignment.Center };
        if (waiting > 0 && onCorner)
        {
            _corner = CornerCount(waiting);
            row.Children.Add(new Panel { Children = { _tile, _corner } });
        }
        else
        {
            row.Children.Add(_tile);
        }
        row.Children.Add(_words);
        if (waiting > 0 && !onCorner)
        {
            _trailing = new Chip(waiting.ToString(CultureInfo.InvariantCulture), Tokens.Accent.Amber) { Height = Tokens.Layout.CountChipHeight };
            row.Children.Add(_trailing);
        }

        _skin = new Border
        {
            Height = look.Height,
            CornerRadius = new CornerRadius(look.Radius),
            BorderThickness = new Thickness(1),
            Padding = look.Padding,
            Child = row,
        };
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        ClipToBounds = false;
        FocusAdorner = null;
        Cursor = new Cursor(StandardCursorType.Hand);
        Template = new FuncControlTemplate<HeldButton>((_, _) => _skin);
        Avalonia.Automation.AutomationProperties.SetName(this, name);
        Paint();
    }

    /// <summary>The section's word.</summary>
    public string Word { get; }

    /// <summary>Whether this is the current section.</summary>
    public bool IsActive
    {
        get => _active;
        set { _active = value; IsTabStop = value; Paint(); }
    }

    /// <summary>The mark alone, its word moved to the tooltip.</summary>
    public bool IsBare
    {
        get => _bare;
        set
        {
            _bare = value;
            _words.IsVisible = !value;
            if (_trailing is not null) _trailing.IsVisible = !value;
            _skin.Padding = value ? new Thickness(_look.Padding.Left, 0) : _look.Padding;
            ToolTip.SetTip(this, value ? Word : null);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty) Paint();
    }

    /// <inheritdoc />
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _keyFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        Paint();
    }

    /// <inheritdoc />
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _keyFocus = false;
        Paint();
    }

    private void Paint()
    {
        var over = IsPointerOver && !_active;
        _skin.Background = _active ? _look.OnFill : over ? _look.HoverFill : _look.RestFill;
        _skin.BorderBrush = _active ? _look.OnEdge : over ? _look.HoverEdge : _look.RestEdge;
        _label.Foreground = _active ? _look.OnInk : over ? _look.HoverInk : _look.RestInk;
        _label.FontWeight = _active ? _look.OnWeight : _look.RestWeight;
        _tile.Opacity = _active || over ? 1 : _look.TileRestOpacity;
        // The count is cut out of the button's own fill, whichever it is wearing (PillNav.tsx, `ground`).
        if (_corner is not null && _skin.Background is ISolidColorBrush ground) _corner.BoxShadow = new BoxShadows(new BoxShadow { Spread = 2, Color = ground.Color });

        var shadows = new List<BoxShadow>();
        if (_keyFocus) shadows.Add(House.FocusHalo);
        if (_active) for (var i = 0; i < _look.OnShadow.Count; i++) shadows.Add(_look.OnShadow[i]);
        _skin.BoxShadow = shadows.Count == 0 ? default : new BoxShadows(shadows[0], [.. shadows.Skip(1)]);
        RenderTransform = new TranslateTransform(0, IsPressed ? 0 : over ? -_look.HoverLift : 0);
    }

    private static TextBlock Words(string text, double size, FontWeight weight) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Sans,
        FontSize = size,
        FontWeight = weight,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    /// <summary>The count on a tile's corner: <c>BADGE_BASE</c> in <c>PillNav.tsx</c>, a 16px amber disc.</summary>
    private static Border CornerCount(int count) => new()
    {
        MinWidth = 16,
        Height = 16,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(4, 0),
        Background = Tokens.Accent.Amber.Fill,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -4, -7, 0),
        Child = new TextBlock
        {
            Text = count.ToString(CultureInfo.InvariantCulture),
            FontFamily = Tokens.Fonts.Sans,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Tokens.Accent.Amber.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };
}

/// <summary>
/// A row of sections of which one is current: one tab stop, the arrow keys move and choose, a
/// click chooses. Every option is this with a different look and a different place to stand.
/// </summary>
internal sealed class SectionGroup : StackPanel
{
    private readonly List<HeldButton> _items = [];
    private readonly bool _alwaysBare;
    private bool _compact;

    /// <summary>Creates the group with the first section current.</summary>
    public SectionGroup(double gap, params HeldButton[] items)
    {
        Orientation = Orientation.Horizontal;
        Spacing = gap;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        ClipToBounds = false;
        foreach (var item in items)
        {
            var index = _items.Count;
            item.Click += (_, _) => Choose(index);
            item.KeyDown += (_, e) =>
            {
                var next = e.Key == Key.Right ? index + 1 : e.Key == Key.Left ? index - 1 : -1;
                if (next < 0 || next >= _items.Count) return;
                Choose(next);
                _items[next].Focus(NavigationMethod.Directional);
                e.Handled = true;
            };
            _items.Add(item);
            Children.Add(item);
        }
        Choose(0);
    }

    /// <summary>The sections, in order.</summary>
    public IReadOnlyList<HeldButton> Items => _items;

    /// <summary>The current section, or -1 when none of these is (Settings open from its own button).</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>
    /// When the row cannot hold every word, the sections that are not current keep their mark and
    /// give up their word, as the shipping tabs do. The Bible: "Nothing clips."
    /// </summary>
    public bool Compact
    {
        get => _compact;
        set { _compact = value; Shape(); }
    }

    /// <summary>Makes <paramref name="index"/> current, or none with -1.</summary>
    public void Choose(int index)
    {
        Selected = index;
        for (var i = 0; i < _items.Count; i++) _items[i].IsActive = i == index;
        Shape();
    }

    private void Shape()
    {
        foreach (var item in _items) item.IsBare = AlwaysBare || (_compact && !item.IsActive);
    }

    /// <summary>Marks only, whatever the width.</summary>
    public bool AlwaysBare
    {
        get => _alwaysBare;
        init { _alwaysBare = value; Shape(); }
    }
}

/// <summary>Contact sheets: shots already saved, cut to the part that matters, tiled and labelled.</summary>
internal static class Sheets
{
    /// <summary>One row of a sheet: its letter and name, one line about it, and a shot for each column.</summary>
    public sealed record Row(string Name, string Line, IReadOnlyList<string> Shots);

    /// <summary>Renders the window and writes it as <paramref name="name"/>.png in <paramref name="dir"/>.</summary>
    public static void Save(string dir, Window window, string name)
    {
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, $"{name}.png"));
    }

    /// <summary>
    /// Tiles the rows under their column heads at real pixel size, each shot cut to
    /// <paramref name="crop"/> pixels from its top, on the app's own canvas.
    /// </summary>
    public static void Write(string dir, string name, IReadOnlyList<string> heads, IReadOnlyList<Row> rows, int crop)
    {
        const int Pad = 24, Label = 46, Head = 30, Gap = 16;
        var tiles = rows.Select(r => r.Shots.Select(s => new Bitmap(Path.Combine(dir, $"{s}.png"))).ToList()).ToList();
        try
        {
            var widths = Enumerable.Range(0, heads.Count).Select(c => tiles.Max(t => t[c].PixelSize.Width)).ToList();
            var heights = tiles.Select(t => t.Max(b => Math.Min(crop, b.PixelSize.Height))).ToList();
            var width = (2 * Pad) + widths.Sum() + (Gap * (heads.Count - 1));
            var height = Pad + Head + heights.Sum(h => Label + h + Gap) + Pad;
            using var sheet = new RenderTargetBitmap(new PixelSize(width, height));
            using (var context = sheet.CreateDrawingContext())
            {
                context.FillRectangle(Tokens.Canvas.Base, new Rect(0, 0, width, height));
                var x = (double)Pad;
                for (var c = 0; c < heads.Count; c++)
                {
                    context.DrawText(Words(heads[c], 12.5, FontWeight.SemiBold, Tokens.Brushes.ColHeader), new Point(x, Pad));
                    x += widths[c] + Gap;
                }
                var y = (double)Pad + Head;
                for (var r = 0; r < rows.Count; r++)
                {
                    var title = Words(rows[r].Name, 16, FontWeight.SemiBold, Tokens.Brushes.Ink);
                    context.DrawText(title, new Point(Pad, y + 2));
                    context.DrawText(Words(rows[r].Line, 12.5, FontWeight.Normal, Tokens.Brushes.Muted), new Point(Pad + title.Width + 12, y + 6));
                    y += Label - 14;
                    x = Pad;
                    for (var c = 0; c < heads.Count; c++)
                    {
                        var size = tiles[r][c].PixelSize;
                        var cut = Math.Min(crop, size.Height);
                        context.DrawImage(tiles[r][c], new Rect(0, 0, size.Width, cut), new Rect(x, y, size.Width, cut));
                        x += widths[c] + Gap;
                    }
                    y += heights[r] + Gap + 14;
                }
            }
            sheet.Save(Path.Combine(dir, $"{name}.png"));
        }
        finally
        {
            foreach (var tile in tiles.SelectMany(t => t)) tile.Dispose();
        }
    }

    private static FormattedText Words(string text, double size, FontWeight weight, IBrush ink) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Tokens.Fonts.Sans, FontStyle.Normal, weight), size, ink);
}
