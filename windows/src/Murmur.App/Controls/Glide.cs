using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Design;

namespace Murmur.App.Controls;

/// <summary>
/// A row of choices with one marker that glides to the current one: a white thumb for the
/// toggle, an underline for the section tabs. The marker is laid out from the chosen item's
/// own bounds, so it fits labels of any width.
/// </summary>
/// <remarks>
/// The marker's place is two styled properties with transitions; layout reads them, so moving
/// the marker is setting them. The first placement is immediate: a window that opens with a
/// thumb sliding in from nowhere is noise, not joy.
/// </remarks>
public sealed class GlidePanel : Panel
{
    /// <summary>The marker's left edge.</summary>
    public static readonly StyledProperty<double> MarkerXProperty = AvaloniaProperty.Register<GlidePanel, double>(nameof(MarkerX));

    /// <summary>The marker's width.</summary>
    public static readonly StyledProperty<double> MarkerWidthProperty = AvaloniaProperty.Register<GlidePanel, double>(nameof(MarkerWidth));

    static GlidePanel() => AffectsArrange<GlidePanel>(MarkerXProperty, MarkerWidthProperty);

    private readonly Control _marker;
    private readonly Panel _row;
    private readonly Func<Control?> _current;
    private readonly Func<Control, Rect> _shape;
    private bool _placed;
    private Rect _target;

    /// <summary>
    /// Creates the panel over <paramref name="row"/>, drawing <paramref name="marker"/> behind it
    /// at the rectangle <paramref name="shape"/> gives for the item <paramref name="current"/> names.
    /// </summary>
    public GlidePanel(Panel row, Control marker, Func<Control?> current, Func<Control, Rect> shape)
    {
        _row = row;
        _marker = marker;
        _current = current;
        _shape = shape;
        Children.Add(marker);
        Children.Add(row);
    }

    /// <inheritdoc cref="MarkerXProperty"/>
    public double MarkerX { get => GetValue(MarkerXProperty); set => SetValue(MarkerXProperty, value); }

    /// <inheritdoc cref="MarkerWidthProperty"/>
    public double MarkerWidth { get => GetValue(MarkerWidthProperty); set => SetValue(MarkerWidthProperty, value); }

    /// <summary>Asks for the marker to move to whichever item is now current.</summary>
    public void Refresh() => InvalidateArrange();

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _row.Measure(availableSize);
        _marker.Measure(availableSize);
        return _row.DesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _row.Arrange(new Rect(finalSize));

        if (_current() is { } item && item.IsVisible)
        {
            var at = _shape(item);
            if (!_placed)
            {
                // Straight there the first time; glide every time after.
                _placed = true;
                MarkerX = at.X;
                MarkerWidth = at.Width;
                if (Tokens.Motion.Animate)
                {
                    var ease = Thumb.Spring;
                    Transitions =
                    [
                        new DoubleTransition { Property = MarkerXProperty, Duration = Tokens.Motion.Glide, Easing = ease },
                        new DoubleTransition { Property = MarkerWidthProperty, Duration = Tokens.Motion.Glide, Easing = ease },
                    ];
                }
            }
            else if (Math.Abs(_target.X - at.X) > 0.1 || Math.Abs(_target.Width - at.Width) > 0.1)
            {
                // A transition in flight reports where it is, not where it's going, so the
                // target is kept: the layout passes the glide itself causes don't restart it.
                MarkerX = at.X;
                MarkerWidth = at.Width;
            }
            _target = at;
            _marker.IsVisible = true;
            // The spring may carry the marker a hair past its mark, never past the bed's edge.
            var width = Math.Clamp(MarkerWidth, 0, finalSize.Width);
            var x = Math.Clamp(MarkerX, 0, Math.Max(0, finalSize.Width - width));
            _marker.Arrange(new Rect(x, at.Y, width, at.Height));
        }
        else
        {
            _marker.IsVisible = false;
        }

        return finalSize;
    }
}

/// <summary>
/// The one toggle (the Bible: "ONE toggle: #eef0f7 bed, no outline"): a soft tinted bed and a
/// white thumb that glides to the choice. An option can carry a small glyph in its own hue,
/// which takes its colour only when chosen, so the colour says which is on.
/// </summary>
/// <remarks>
/// Selection is the thumb's elevation and the ink, never a coloured fill. Used for the mode in
/// the title row and every either-or in Settings and the editor, so the same job looks the same.
/// </remarks>
public sealed class Segmented : Border
{
    /// <summary>One choice: its words, and optionally a glyph, the hue it takes when chosen and a tooltip.</summary>
    public sealed record Choice(string Label, string? Icon = null, Tokens.Accent? Accent = null, string? Tip = null);

    private readonly List<Item> _items = [];
    private readonly GlidePanel _glide;
    private int _active = -1;

    /// <summary>Raised with the index of the chosen segment, when a person chooses it.</summary>
    public event EventHandler<int>? Selected;

    /// <summary>Builds the control from plain labels.</summary>
    public Segmented(IEnumerable<string> labels, int selected = 0)
        : this(labels.Select(l => new Choice(l)), selected)
    {
    }

    /// <summary>Builds the control.</summary>
    public Segmented(IEnumerable<Choice> options, int selected = 0)
    {
        Background = Tokens.Brushes.ToggleBed;
        CornerRadius = new CornerRadius(Tokens.Radius.Button);
        Padding = new Thickness(Tokens.Space.TrackInset);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Hair };
        var index = 0;
        foreach (var option in options)
        {
            var item = new Item(option, index++);
            item.PointerPressed += (_, e) =>
            {
                if (!IsEnabled || !e.GetCurrentPoint(item).Properties.IsLeftButtonPressed) return;
                Choose(item.Index);
            };
            item.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) { Choose(item.Index); e.Handled = true; }
                else if (e.Key == Key.Right && item.Index + 1 < _items.Count) { _items[item.Index + 1].Focus(); Choose(item.Index + 1); e.Handled = true; }
                else if (e.Key == Key.Left && item.Index > 0) { _items[item.Index - 1].Focus(); Choose(item.Index - 1); e.Handled = true; }
            };
            _items.Add(item);
            row.Children.Add(item);
        }

        _glide = new GlidePanel(row, Thumb.Make(), () => _active >= 0 && _active < _items.Count ? _items[_active] : null, item => item.Bounds);
        Child = _glide;
        Select(selected);
    }

    /// <summary>The chosen segment.</summary>
    public int SelectedIndex => _active;

    /// <summary>The segments' words, in order. For tests and tooltips.</summary>
    public IReadOnlyList<string> Labels => [.. _items.Select(i => i.Option.Label)];

    private void Choose(int index)
    {
        if (index == _active) return;
        Select(index);
        Selected?.Invoke(this, index);
    }

    /// <summary>Sets the active segment without raising <see cref="Selected"/>.</summary>
    public void Select(int index)
    {
        _active = index;
        foreach (var item in _items) item.Paint(item.Index == index);
        _glide.Refresh();
    }

    /// <summary>Chooses a segment as a click would. Exposed for headless tests.</summary>
    public void Press(int index) => Choose(index);

    private sealed class Item : Border
    {
        private readonly SegmentFace _face;

        public Item(Choice option, int index)
        {
            Option = option;
            Index = index;
            Focusable = true;
            Height = Tokens.Layout.SegmentHeight;
            Cursor = new Cursor(StandardCursorType.Hand);
            _face = new SegmentFace(this, option.Label, option.Icon, option.Accent, Tokens.Fonts.Small);
            Child = _face.Content;
            Padding = _face.Padding;
            if (option.Tip is { } tip) ToolTip.SetTip(this, tip);
            Avalonia.Automation.AutomationProperties.SetName(this, option.Label);
        }

        public Choice Option { get; }

        public int Index { get; }

        public void Paint(bool on) => _face.Paint(on);
    }
}

/// <summary>
/// The thumb every toggle and the tabs share: white, a little rounder than the bed's inside, and
/// lifted on a soft shadow tinted with the brand rather than grey, so it reads as a small held
/// thing sitting on the bed, not a box drawn on it.
/// </summary>
internal static class Thumb
{
    public static Border Make() => new()
    {
        Background = Tokens.Brushes.Card,
        CornerRadius = new CornerRadius(Tokens.Radius.Segment),
        BoxShadow = Tokens.Shadow.Thumb,
    };

    /// <summary>A glide that settles with the smallest spring past its mark, then home.</summary>
    public static Easing Spring { get; } = new SoftSpring();

    private sealed class SoftSpring : Easing
    {
        // Back-out with a gentle overshoot: a couple of per cent past the mark, then home.
        private const double Overshoot = 0.5;

        public override double Ease(double progress)
        {
            var p = progress - 1;
            return 1 + (Overshoot + 1) * p * p * p + Overshoot * p * p;
        }
    }
}

/// <summary>
/// The words and mark inside one segment or tab, and how they answer: the mark in its own hue
/// (fuller when chosen, softer when not), the ink deepening on hover with a whisper of white
/// behind it, and a small press that gives under the pointer.
/// </summary>
internal sealed class SegmentFace
{
    private readonly Border _host;
    private readonly TextBlock _label;
    private readonly Glyph? _glyph;
    private readonly Tokens.Accent? _accent;
    private bool _on;

    public SegmentFace(Border host, string text, string? icon, Tokens.Accent? accent, double size)
    {
        _host = host;
        _accent = accent;
        _label = new TextBlock
        {
            Text = text,
            FontFamily = Tokens.Fonts.Sans,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Chip, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
        {
            _glyph = new Glyph(icon, 13) { Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = Tokens.Motion.Quick }] };
            Content.Children.Add(_glyph);
        }
        Content.Children.Add(_label);
        Padding = new Thickness(icon is null ? Tokens.Layout.NavPillPadX : Tokens.Layout.NavPillPadX - 3, 0, Tokens.Layout.NavPillPadX, 0);

        host.Background = Tokens.Brushes.None;
        host.CornerRadius = new CornerRadius(Tokens.Radius.Segment);
        host.RenderTransform = new ScaleTransform(1, 1);
        host.RenderTransformOrigin = RelativePoint.Center;
        host.Transitions =
        [
            new BrushTransition { Property = Border.BackgroundProperty, Duration = Tokens.Motion.Quick },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Tokens.Motion.Press },
        ];
        host.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsPointerOverProperty) Repaint(); };
        host.AddHandler(InputElement.PointerPressedEvent, (_, _) => Press(true), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Press(false), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Press(false), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Repaint();
    }

    public StackPanel Content { get; }

    public Thickness Padding { get; }

    public TextBlock Label => _label;

    public void Paint(bool on)
    {
        _on = on;
        Repaint();
    }

    private void Press(bool down) =>
        _host.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(down ? $"scale({Tokens.Motion.SegmentPress.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : "scale(1)");

    private void Repaint()
    {
        var over = _host.IsPointerOver;
        _label.Foreground = _on ? Tokens.Brushes.Ink : over ? Tokens.Brushes.BrandStrong : Tokens.Brushes.Muted;
        // The thumb is the chosen one's surface; an unchosen one under the pointer gets a whisper
        // of it, so the control answers before it is clicked.
        _host.Background = !_on && over ? Tokens.Brushes.SegmentHover : Tokens.Brushes.None;
        if (_glyph is not null)
        {
            _glyph.Ink = _accent?.Ink ?? Tokens.Brushes.BrandStrong;
            _glyph.Opacity = _on || over ? 1 : Tokens.Opacity.GlyphResting;
        }
    }
}

/// <summary>
/// One section in the masthead's tabs: held on the house bed with a white thumb that glides to the
/// current one, the same shape as every toggle, each section led by its own small tinted mark. A
/// count rides beside the name as a chip.
/// </summary>
/// <remarks>
/// Dave, 05/10/2026, on an underline under bare words: it read as floating text. Words always sit
/// on a control or in a held shape, so the sections are held again, slimmer and paler than the old
/// grey tray, with the thumb moving rather than jumping.
/// </remarks>
public sealed class NavLink : Button
{
    private readonly Chip _count;
    private readonly Border _surface;
    private readonly SegmentFace _face;
    private bool _active;

    /// <summary>Creates the link, optionally led by <paramref name="icon"/> in <paramref name="accent"/>'s hue.</summary>
    public NavLink(string text, string? icon = null, Tokens.Accent? accent = null)
    {
        _surface = new Border { Height = Tokens.Layout.SegmentHeight };
        _face = new SegmentFace(_surface, text, icon, accent, Tokens.Fonts.Tab);
        _count = new Chip(string.Empty, Tokens.Accent.Amber) { IsVisible = false, Height = Tokens.Layout.CountChipHeight };
        _face.Content.Children.Add(_count);
        _surface.Padding = _face.Padding;
        _surface.Child = _face.Content;
        Background = Tokens.Brushes.None;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Cursor = new Cursor(StandardCursorType.Hand);
        Template = new FuncControlTemplate<NavLink>((_, _) => _surface);
        Avalonia.Automation.AutomationProperties.SetName(this, text);
    }

    /// <summary>The link's text.</summary>
    public string Text
    {
        get => _face.Label.Text ?? string.Empty;
        set => _face.Label.Text = value;
    }

    /// <summary>Something waiting in the section, shown as a chip beside the label; nothing at zero.</summary>
    public int Count
    {
        get => int.TryParse(_count.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        set
        {
            _count.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _count.IsVisible = value > 0;
        }
    }

    /// <summary>The tabs: the links on the house bed, the white thumb gliding to the current one.</summary>
    public static Border Track(params NavLink[] links)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Hair };
        foreach (var link in links) row.Children.Add(link);
        var glide = new GlidePanel(row, Thumb.Make(), () => links.FirstOrDefault(l => l.IsActive), link => link.Bounds);
        foreach (var link in links) link._glide = glide;
        return new Border
        {
            Background = Tokens.Brushes.ToggleBed,
            CornerRadius = new CornerRadius(Tokens.Radius.Button),
            Padding = new Thickness(Tokens.Space.TrackInset),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = glide,
        };
    }

    private GlidePanel? _glide;

    /// <summary>Whether this link is the current section.</summary>
    public bool IsActive
    {
        get => _active;
        set { _active = value; _face.Paint(value); _glide?.Refresh(); }
    }
}
