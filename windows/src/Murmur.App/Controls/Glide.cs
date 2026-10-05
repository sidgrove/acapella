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
                    var ease = new CubicEaseOut();
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
            _marker.Arrange(new Rect(MarkerX, at.Y, Math.Max(0, MarkerWidth), at.Height));
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

        var thumb = new Border
        {
            Background = Tokens.Brushes.Card,
            CornerRadius = new CornerRadius(Tokens.Radius.Segment),
            BoxShadow = Tokens.Shadow.NavActive,
        };
        _glide = new GlidePanel(row, thumb, () => _active >= 0 && _active < _items.Count ? _items[_active] : null, item => item.Bounds);
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
        private readonly TextBlock _label;
        private readonly Glyph? _glyph;
        private bool _on;

        public Item(Choice option, int index)
        {
            Option = option;
            Index = index;
            Focusable = true;
            Height = Tokens.Layout.SegmentHeight;
            Padding = new Thickness(option.Icon is null ? Tokens.Layout.NavPillPadX : Tokens.Layout.NavPillPadX - 3, 0, Tokens.Layout.NavPillPadX, 0);
            Background = Tokens.Brushes.None;
            Cursor = new Cursor(StandardCursorType.Hand);

            _label = new TextBlock
            {
                Text = option.Label,
                FontFamily = Tokens.Fonts.Sans,
                FontSize = Tokens.Fonts.Small,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Chip, VerticalAlignment = VerticalAlignment.Center };
            if (option.Icon is { } icon)
            {
                _glyph = new Glyph(icon, 13);
                content.Children.Add(_glyph);
            }
            content.Children.Add(_label);
            Child = content;
            if (option.Tip is { } tip) ToolTip.SetTip(this, tip);
            Avalonia.Automation.AutomationProperties.SetName(this, option.Label);
        }

        public Choice Option { get; }

        public int Index { get; }

        public void Paint(bool on)
        {
            _on = on;
            Repaint();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsPointerOverProperty) Repaint();
        }

        private void Repaint()
        {
            _label.Foreground = _on ? Tokens.Brushes.Ink : IsPointerOver ? Tokens.Brushes.BrandStrong : Tokens.Brushes.Muted;
            if (_glyph is not null)
                _glyph.Ink = _on ? (Option.Accent?.Ink ?? Tokens.Brushes.BrandStrong) : Tokens.Brushes.Faint;
        }
    }
}

/// <summary>
/// One section in the masthead's tabs: held on the house bed with a white thumb that glides to the
/// current one, the same shape as every toggle. A count rides beside the name as a chip.
/// </summary>
/// <remarks>
/// Dave, 05/10/2026, on an underline under bare words: it read as floating text. Words always sit
/// on a control or in a held shape, so the sections are held again, slimmer and paler than the old
/// grey tray, with the thumb moving rather than jumping.
/// </remarks>
public sealed class NavLink : Button
{
    private readonly TextBlock _label;
    private readonly Chip _count;
    private readonly Border _surface;
    private bool _active;

    /// <summary>Creates the link.</summary>
    public NavLink(string text)
    {
        _label = new TextBlock
        {
            Text = text,
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Tab,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _count = new Chip(string.Empty, Tokens.Accent.Amber) { IsVisible = false, Height = Tokens.Layout.CountChipHeight };
        _surface = new Border
        {
            Background = Tokens.Brushes.None,
            Height = Tokens.Layout.SegmentHeight,
            Padding = new Thickness(Tokens.Layout.NavPillPadX, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Chip, VerticalAlignment = VerticalAlignment.Center, Children = { _label, _count } },
        };
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
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
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
        var thumb = new Border { Background = Tokens.Brushes.Card, CornerRadius = new CornerRadius(Tokens.Radius.Segment), BoxShadow = Tokens.Shadow.NavActive };
        var glide = new GlidePanel(row, thumb, () => links.FirstOrDefault(l => l.IsActive), link => link.Bounds);
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
        set { _active = value; Paint(); _glide?.Refresh(); }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty) Paint();
    }

    private void Paint() =>
        _label.Foreground = _active ? Tokens.Brushes.Ink : IsPointerOver ? Tokens.Brushes.BrandStrong : Tokens.Brushes.Muted;
}
