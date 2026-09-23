using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.App.Design;

namespace Murmur.App.Controls;

/// <summary>A quiet ambient wash and faint grid behind opaque content cards.</summary>
public sealed class WashPanel : Decorator
{
    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.FillRectangle(Tokens.Brushes.Wash, bounds);
        context.FillRectangle(Tokens.Brushes.AmbientPeri, bounds);
        context.FillRectangle(Tokens.Brushes.AmbientPeach, bounds);
        var grid = new Pen(Tokens.Brushes.GridLine, Tokens.Border.Hairline);
        for (var x = Tokens.Layout.GridPitch; x < bounds.Width; x += Tokens.Layout.GridPitch)
            context.DrawLine(grid, new Point(x, 0), new Point(x, bounds.Height));
        for (var y = Tokens.Layout.GridPitch; y < bounds.Height; y += Tokens.Layout.GridPitch)
            context.DrawLine(grid, new Point(0, y), new Point(bounds.Width, y));
    }
}

/// <summary>Card surfaces. <c>.sg-card</c>: opaque white, hairline border, tight shadow.</summary>
public static class Card
{
    /// <summary>A card at rest.</summary>
    public static Border Standard(Control content, double? padding = null) => new()
    {
        Background = Tokens.Brushes.Card,
        BorderBrush = Tokens.Brushes.CardBorder,
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Card),
        BoxShadow = Tokens.Shadow.Card,
        Padding = new Thickness(padding ?? Tokens.Space.Card),
        Child = content,
    };

    /// <summary>A card that lifts 1px under the pointer. <c>.sg-card[data-hoverable]</c>.</summary>
    public static Border Lifting(Control content, double? padding = null)
    {
        var card = Standard(content, padding);
        card.RenderTransform = new TranslateTransform();
        card.Transitions =
        [
            new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = Tokens.Motion.Lift },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = Tokens.Motion.Lift },
        ];
        card.PointerEntered += (_, _) =>
        {
            card.BoxShadow = Tokens.Shadow.CardHover;
            card.BorderBrush = Tokens.Brushes.CardBorderStrong;
            card.RenderTransform = new TranslateTransform(0, -Tokens.Motion.CardLift);
        };
        card.PointerExited += (_, _) =>
        {
            card.BoxShadow = Tokens.Shadow.Card;
            card.BorderBrush = Tokens.Brushes.CardBorder;
            card.RenderTransform = new TranslateTransform();
        };
        return card;
    }

    /// <summary>An inner panel. <c>.sg-card[data-variant="subtle"]</c>.</summary>
    public static Border Subtle(Control content, double? padding = null) => new()
    {
        Background = Tokens.Brushes.Surface,
        BorderBrush = Tokens.Brushes.None,
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Inner),
        Padding = new Thickness(padding ?? Tokens.Space.Roomy),
        Child = content,
    };

    /// <summary>A tinted notice: rose for attention, amber for a warning.</summary>
    public static Border Notice(Control content, IBrush background, IBrush edge) => new()
    {
        Background = background,
        BorderBrush = edge,
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Inner),
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Base),
        Child = content,
    };

    /// <summary>The empty-state card. <c>.sg-empty</c>: white, centred, generous.</summary>
    public static Border Empty(Control content) => new()
    {
        Background = Tokens.Brushes.Card,
        BorderBrush = Tokens.Brushes.Hairline,
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Card),
        BoxShadow = Tokens.Shadow.Empty,
        Padding = new Thickness(Tokens.Space.Section, Tokens.Space.Empty),
        Child = content,
    };
}

/// <summary>Text at the type scale, one font per role.</summary>
public static class Text
{
    /// <summary>A page title. <c>.sg-hero-title</c>: Very Vogue Text 32px 400, positive tracking.</summary>
    public static TextBlock Title(string text) =>
        Make(text, Tokens.Fonts.Serif, Tokens.Fonts.Title, FontWeight.Normal, Tokens.Brushes.Ink, Tokens.Fonts.TitleTracking);

    /// <summary>The caption-strip title, the same face smaller.</summary>
    public static TextBlock CaptionTitle(string text) =>
        Make(text, Tokens.Fonts.Serif, Tokens.Fonts.CaptionTitle, FontWeight.Normal, Tokens.Brushes.Ink, Tokens.Fonts.TitleTracking * (Tokens.Fonts.CaptionTitle / Tokens.Fonts.Title));

    /// <summary>A section heading. <c>.display</c>: DM Sans 700, tight.</summary>
    public static TextBlock Heading(string text) =>
        Make(text, Tokens.Fonts.Sans, Tokens.Fonts.Heading, FontWeight.Bold, Tokens.Brushes.Ink, Tokens.Fonts.HeadingTracking);

    /// <summary>Body copy.</summary>
    public static TextBlock Body(string text) => Make(text, Tokens.Fonts.Sans, Tokens.Fonts.Base, FontWeight.Normal, Tokens.Brushes.Ink);

    /// <summary>Body copy, emphasised.</summary>
    public static TextBlock BodyStrong(string text) => Make(text, Tokens.Fonts.Sans, Tokens.Fonts.Base, FontWeight.Bold, Tokens.Brushes.Ink);

    /// <summary>The transcript itself.</summary>
    public static TextBlock Reading(string text) =>
        Make(text, Tokens.Fonts.Sans, Tokens.Fonts.Reading, FontWeight.Normal, Tokens.Brushes.Ink, lineHeight: Tokens.Fonts.Reading * 1.55);

    /// <summary>Secondary copy.</summary>
    public static TextBlock Muted(string text) => Make(text, Tokens.Fonts.Sans, Tokens.Fonts.Body, FontWeight.Normal, Tokens.Brushes.Muted);

    /// <summary>Metadata and captions.</summary>
    public static TextBlock Caption(string text) => Make(text, Tokens.Fonts.Sans, Tokens.Fonts.Caption, FontWeight.Normal, Tokens.Brushes.Faint);

    /// <summary>An eyebrow label. <c>.sg-hero-eyebrow</c>: 11px, sentence case, muted.</summary>
    public static TextBlock Eyebrow(string text) =>
        Make(text, Tokens.Fonts.Sans, Tokens.Fonts.Eyebrow, FontWeight.Medium, Tokens.Brushes.Muted, Tokens.Fonts.EyebrowTracking);

    /// <summary>A number that ticks, in DM Sans with tabular figures. <c>.num</c>.</summary>
    public static TextBlock Number(string text, double size, IBrush brush)
    {
        var block = Make(text, Tokens.Fonts.Sans, size, FontWeight.Bold, brush);
        block.FontFeatures = Tokens.Fonts.Tabular;
        return block;
    }

    /// <summary>The hero number, in Perfectly Nineties. <c>.serif-num</c>.</summary>
    public static TextBlock Hero(string text)
    {
        var block = Make(text, Tokens.Fonts.Display, Tokens.Fonts.Hero, FontWeight.Normal, Tokens.Brushes.Ink);
        block.FontFeatures = Tokens.Fonts.Tabular;
        return block;
    }

    private static TextBlock Make(string text, FontFamily family, double size, FontWeight weight, IBrush brush, double tracking = 0, double? lineHeight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = family,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            LetterSpacing = tracking,
            TextWrapping = TextWrapping.Wrap,
        };
        if (lineHeight is { } lh) block.LineHeight = lh;
        return block;
    }
}

/// <summary>A chip. <c>.sg-*</c> chip treatment: 10.5px 600, 0.02em, pill, tinted.</summary>
public static class Pill
{
    /// <summary>Brand-tinted.</summary>
    public static Border Brand(string text) => Make(text, Tokens.Brushes.BrandStrong, Tokens.Brushes.BrandLight);

    /// <summary>Amber.</summary>
    public static Border Amber(string text) => Make(text, Tokens.Brushes.Amber, Tokens.Brushes.AmberLight);

    /// <summary>Rose.</summary>
    public static Border Rose(string text) => Make(text, Tokens.Brushes.Rose, Tokens.Brushes.RoseLight);

    /// <summary>Neutral.</summary>
    public static Border Neutral(string text) => Make(text, Tokens.Brushes.Muted, Tokens.Brushes.Surface);

    private static Border Make(string text, IBrush foreground, IBrush background) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(Tokens.Radius.Pill),
        Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair + Tokens.Border.Hairline),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Badge,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = Tokens.Fonts.BadgeTracking,
            Foreground = foreground,
        },
    };
}

/// <summary>
/// A button in one of the shapes the app and the site actually ship.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Primary</b> — <c>.sg-btn-brand</c>: pale lavender pill, brand-strong bold text,
/// pale border. No gradient. The app's default button.</item>
/// <item><b>Hero</b> — the site's <c>.button</c>: solid brand-strong pill, white text, a lit
/// top edge and a shadow ramp; deepens to ink on hover, settles 1px on press. One per screen.</item>
/// <item><b>Ghost</b> — <c>.me-btn-ghost</c>: white pill, panel border, muted text.</item>
/// <item><b>Quiet</b> — the kit's <c>ghost</c>: nothing until hovered.</item>
/// <item><b>Danger</b> — the kit's <c>destructive</c>: rose text on rose at 10%.</item>
/// </list>
/// Every variant presses with a 1px travel, as the kit's <c>active:translate-y-px</c>.
/// </remarks>
public sealed class SgButton : Button
{
    /// <summary>The variants.</summary>
    public enum Kind
    {
        /// <summary>The app's default: pale lavender pill.</summary>
        Primary,

        /// <summary>The site's solid CTA pill.</summary>
        Hero,

        /// <summary>White pill with a border.</summary>
        Ghost,

        /// <summary>Text until hovered.</summary>
        Quiet,

        /// <summary>Rose, for delete.</summary>
        Danger,
    }

    private readonly Kind _kind;
    private readonly Border _skin = new();

    /// <summary>Creates a button with a text label, or any content.</summary>
    public SgButton(object label, Kind kind = Kind.Ghost, bool compact = false)
    {
        _kind = kind;
        Content = label;

        var hero = kind == Kind.Hero;
        FontFamily = Tokens.Fonts.Sans;
        FontSize = hero ? Tokens.Fonts.HeroButton : compact ? Tokens.Fonts.Small : Tokens.Fonts.Body;
        FontWeight = kind is Kind.Primary or Kind.Hero ? FontWeight.Bold : FontWeight.SemiBold;
        Height = hero ? Tokens.Layout.HeroButtonHeight : compact ? Tokens.Layout.ButtonHeightSmall : Tokens.Layout.ButtonHeight;
        Padding = hero
            ? new Thickness(Tokens.Layout.HeroPadLeft, 0, Tokens.Layout.HeroPadRight, 0)
            : new Thickness(compact ? Tokens.Layout.ButtonPadXSmall : Tokens.Layout.ButtonPadX, 0);
        Background = Tokens.Brushes.None;
        BorderThickness = new Thickness(0);
        // The pill's shadow lives on the inner Border; without this the Button clips it to
        // its own rectangle, and a soft shadow turns into a hard-edged box.
        ClipToBounds = false;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        RenderTransform = new TranslateTransform();
        Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = Tokens.Motion.Press }];

        _skin.CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        _skin.BorderThickness = new Thickness(Tokens.Border.Hairline);
        _skin.Transitions =
        [
            new BrushTransition { Property = Border.BackgroundProperty, Duration = Tokens.Motion.Quick },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = Tokens.Motion.Quick },
            new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = hero ? Tokens.Motion.Travel : Tokens.Motion.Quick },
        ];

        Template = new FuncControlTemplate<SgButton>((button, scope) =>
        {
            var presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = button[!ContentProperty],
                [!ContentPresenter.PaddingProperty] = button[!PaddingProperty],
                [!ContentPresenter.ForegroundProperty] = button[!ForegroundProperty],
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            }.RegisterInNameScope(scope);

            // The hero's lit top edge is a hairline inside the pill, not a gradient.
            if (hero)
            {
                _skin.Child = new Panel
                {
                    Children =
                    {
                        new Border
                        {
                            Height = Tokens.Border.Hairline,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(Tokens.Layout.HeroPadX / 2, 0),
                            Background = Tokens.Brushes.HeroHighlight,
                            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
                        },
                        presenter,
                    },
                };
            }
            else
            {
                _skin.Child = presenter;
            }

            return _skin;
        });

        Paint();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsPressedProperty || change.Property == IsPointerOverProperty || change.Property == IsEnabledProperty)
        {
            Paint();
        }
    }

    private void Paint()
    {
        var over = IsPointerOver && IsEnabled;
        var down = IsPressed;

        switch (_kind)
        {
            case Kind.Primary:
                _skin.Background = over ? Tokens.Brushes.PillFillHover : Tokens.Brushes.PillFill;
                _skin.BorderBrush = over ? Tokens.Brushes.BrandMid : Tokens.Brushes.PillBorder;
                _skin.BoxShadow = Tokens.Shadow.Button;
                Foreground = Tokens.Brushes.BrandStrong;
                break;
            case Kind.Hero:
                _skin.Background = over ? Tokens.Brushes.InkSite : Tokens.Brushes.BrandStrong;
                _skin.BorderBrush = Tokens.Brushes.None;
                _skin.BoxShadow = down ? Tokens.Shadow.HeroPressed : over ? Tokens.Shadow.HeroHover : Tokens.Shadow.Hero;
                Foreground = Tokens.Brushes.OnBrand;
                break;
            case Kind.Ghost:
                _skin.Background = Tokens.Brushes.Card;
                _skin.BorderBrush = over ? Tokens.Brushes.CardBorderStrong : Tokens.Brushes.PanelBorder;
                _skin.BoxShadow = Tokens.Shadow.Button;
                Foreground = over ? Tokens.Brushes.BrandStrong : Tokens.Brushes.Muted;
                break;
            case Kind.Quiet:
                _skin.Background = over ? Tokens.Brushes.Surface : Tokens.Brushes.None;
                _skin.BorderBrush = Tokens.Brushes.None;
                _skin.BoxShadow = Tokens.Shadow.None;
                Foreground = over ? Tokens.Brushes.BrandStrong : Tokens.Brushes.Muted;
                break;
            case Kind.Danger:
                _skin.Background = over ? Tokens.Brushes.RoseLight : Tokens.Brushes.None;
                _skin.BorderBrush = Tokens.Brushes.None;
                _skin.BoxShadow = Tokens.Shadow.None;
                Foreground = Tokens.Brushes.Rose;
                break;
        }

        // Hero rises 2px on hover and settles 1px below rest on press; everything else
        // only travels 1px on press.
        var y = down ? Tokens.Motion.PressTravel : (_kind == Kind.Hero && over ? -Tokens.Motion.HeroLift : 0);
        RenderTransform = new TranslateTransform(0, y);

        Opacity = IsEnabled ? 1 : Tokens.Opacity.Disabled;
    }
}

/// <summary>A minimal window control: a thin glyph that gains a surface tint on hover.</summary>
public sealed class CaptionGlyph : Button
{
    /// <summary>What the glyph shows.</summary>
    public enum Glyph
    {
        /// <summary>A short bar.</summary>
        Minimise,

        /// <summary>A hollow square.</summary>
        Maximise,

        /// <summary>A cross.</summary>
        Close,
    }

    private readonly Glyph _kind;

    /// <summary>Creates a glyph button.</summary>
    public CaptionGlyph(Glyph kind)
    {
        _kind = kind;
        Width = Tokens.Layout.CaptionButton;
        Height = Tokens.Layout.CaptionButton;
        Background = Tokens.Brushes.None;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty) InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var hover = IsPointerOver;
        var close = _kind == Glyph.Close;

        if (hover)
        {
            context.DrawEllipse(close ? Tokens.Brushes.RoseLight : Tokens.Brushes.Surface, null,
                bounds.Center, bounds.Width / 2, bounds.Height / 2);
        }

        var pen = new Pen(hover && close ? Tokens.Brushes.Rose : Tokens.Brushes.Muted, Tokens.Border.Hairline * 1.25);
        var c = bounds.Center;
        var r = Tokens.Layout.CaptionButton * 0.16;

        switch (_kind)
        {
            case Glyph.Minimise:
                context.DrawLine(pen, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
                break;
            case Glyph.Maximise:
                context.DrawRectangle(null, pen, new RoundedRect(new Rect(c.X - r, c.Y - r, 2 * r, 2 * r), Tokens.Space.Hair));
                break;
            case Glyph.Close:
                context.DrawLine(pen, new Point(c.X - r, c.Y - r), new Point(c.X + r, c.Y + r));
                context.DrawLine(pen, new Point(c.X - r, c.Y + r), new Point(c.X + r, c.Y - r));
                break;
        }
    }
}

/// <summary>The kit's switch: 32×18 pill, brand-strong when on, white thumb.</summary>
public sealed class Switch : ToggleButton
{
    /// <summary>Creates a switch.</summary>
    public Switch()
    {
        Width = Tokens.Layout.SwitchWidth;
        Height = Tokens.Layout.SwitchHeight;
        Background = Tokens.Brushes.None;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Template = new FuncControlTemplate<Switch>((_, _) => new Panel());
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty || change.Property == IsPointerOverProperty) InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var on = IsChecked == true;
        var bounds = new Rect(Bounds.Size);

        context.DrawRectangle(on ? Tokens.Brushes.BrandStrong : (IsPointerOver ? Tokens.Brushes.CardBorderStrong : Tokens.Brushes.Line), null,
            new RoundedRect(bounds, Tokens.Radius.Pill));

        var pad = Tokens.Border.Hairline;
        var d = bounds.Height - 2 * pad;
        var x = on ? bounds.Width - pad - d / 2 : pad + d / 2;
        context.DrawEllipse(Tokens.Brushes.Card, null, new Point(x, bounds.Height / 2), d / 2, d / 2);
    }
}

/// <summary>A small status dot, optionally breathing when live.</summary>
public sealed class StatusDot : Control
{
    /// <summary>The colour.</summary>
    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<StatusDot, IBrush>(nameof(Fill), Tokens.Brushes.BrandMid);

    /// <summary>Whether the dot pulses.</summary>
    public static readonly StyledProperty<bool> IsLiveProperty =
        AvaloniaProperty.Register<StatusDot, bool>(nameof(IsLive));

    /// <inheritdoc cref="FillProperty"/>
    public IBrush Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <inheritdoc cref="IsLiveProperty"/>
    public bool IsLive
    {
        get => GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    private DispatcherTimer? _ticker;
    private double _phase;

    static StatusDot() => AffectsRender<StatusDot>(FillProperty, IsLiveProperty);

    /// <summary>Creates a dot at the token size.</summary>
    public StatusDot()
    {
        Width = Tokens.Layout.Dot * 2;
        Height = Tokens.Layout.Dot * 2;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = Tokens.Motion.Frame };
        _ticker.Tick += (_, _) => { if (IsLive) { _phase += 0.08; InvalidateVisual(); } };
        _ticker.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ticker?.Stop();
        _ticker = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = Tokens.Layout.Dot / 2;

        if (IsLive && Fill is ISolidColorBrush solid)
        {
            var halo = r + (Tokens.Layout.Dot / 2) * (0.5 + 0.5 * Math.Sin(_phase));
            context.DrawEllipse(new SolidColorBrush(solid.Color, Tokens.Opacity.Hairline), null, c, halo, halo);
        }

        context.DrawEllipse(Fill, null, c, r, r);
    }
}

/// <summary>
/// The listening bars: a row of rounded bars that move with the input level.
/// </summary>
/// <remarks>
/// Each bar has its own weight and phase so the row reads as a waveform. Rises quickly,
/// falls slowly, breathes gently while idle. Physics live in plain fields stepped by a timer.
/// </remarks>
public sealed class LevelBars : Control
{
    /// <summary>Current input level, 0…1.</summary>
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<LevelBars, double>(nameof(Level));

    /// <summary>Whether recording — bars go brand and follow the level.</summary>
    public static readonly StyledProperty<bool> IsLiveProperty =
        AvaloniaProperty.Register<LevelBars, bool>(nameof(IsLive));

    /// <inheritdoc cref="LevelProperty"/>
    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <inheritdoc cref="IsLiveProperty"/>
    public bool IsLive
    {
        get => GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    private readonly int _count;
    private readonly double[] _heights;
    private readonly double[] _weights;
    private readonly double[] _phases;
    private DispatcherTimer? _ticker;
    private double _time;

    static LevelBars() => AffectsRender<LevelBars>(IsLiveProperty);

    /// <summary>Creates a row of bars.</summary>
    public LevelBars(int count, double height)
    {
        _count = count;
        _heights = new double[count];
        _weights = new double[count];
        _phases = new double[count];

        for (var i = 0; i < count; i++)
        {
            var x = (i + 0.5) / count;
            _weights[i] = 0.45 + 0.55 * Math.Sin(x * Math.PI);
            _phases[i] = (i * 2.399) % (2 * Math.PI);
        }

        Width = count * (Tokens.Layout.BarWidth + Tokens.Layout.BarGap) - Tokens.Layout.BarGap;
        Height = height;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = Tokens.Motion.Frame };
        _ticker.Tick += (_, _) => { Step(); InvalidateVisual(); };
        _ticker.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ticker?.Stop();
        _ticker = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Step()
    {
        _time += 0.05;
        var level = Math.Clamp(Math.Sqrt(Math.Clamp(Level, 0, 1)) * 1.6, 0, 1);

        for (var i = 0; i < _count; i++)
        {
            var wobble = 0.65 + 0.35 * Math.Sin(_time * 3.1 + _phases[i]);
            var target = IsLive
                ? Tokens.Layout.BarMinFraction + (1 - Tokens.Layout.BarMinFraction) * level * _weights[i] * wobble
                : Tokens.Layout.BarMinFraction + Tokens.Motion.BarIdleBreath * (0.5 + 0.5 * Math.Sin(_time * 1.2 + _phases[i]));

            var rate = target > _heights[i] ? Tokens.Motion.BarAttack : Tokens.Motion.BarRelease;
            _heights[i] += (target - _heights[i]) * rate;
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var pitch = Tokens.Layout.BarWidth + Tokens.Layout.BarGap;
        var brush = IsLive ? Tokens.Brushes.BrandStrong : Tokens.Brushes.BarsIdle;

        for (var i = 0; i < _count; i++)
        {
            var h = Math.Max(Tokens.Layout.BarWidth, _heights[i] * bounds.Height);
            var rect = new Rect(i * pitch, (bounds.Height - h) / 2, Tokens.Layout.BarWidth, h);
            context.DrawRectangle(brush, null, new RoundedRect(rect, Tokens.Radius.Bar));
        }
    }
}

/// <summary>The abstract audio mark shared with the desktop app icon.</summary>
public sealed class LogoMark : Control
{
    private static readonly Lazy<Avalonia.Media.Imaging.Bitmap> Artwork = new(() =>
    {
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Acapella/Assets/app-icon.png"));
        return new Avalonia.Media.Imaging.Bitmap(stream);
    });

    /// <summary>Creates the mark at the token size.</summary>
    public LogoMark()
    {
        Width = Tokens.Layout.LogoTile;
        Height = Tokens.Layout.LogoTile;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var artwork = Artwork.Value;
        var scale = Math.Min(Bounds.Width / artwork.Size.Width, Bounds.Height / artwork.Size.Height);
        var size = new Size(artwork.Size.Width * scale, artwork.Size.Height * scale);
        var destination = new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
        context.DrawImage(artwork, new Rect(artwork.Size), destination);
    }
}
/// <summary>
/// The pill nav. <c>PillNav.tsx</c>: a translucent ink bed, the active pill solid white and
/// lifted, bold brand-strong text; inactive muted. Selection by elevation and weight, never hue.
/// </summary>
public sealed class Segmented : Border
{
    private readonly List<(Border Pill, TextBlock Label)> _segments = [];

    /// <summary>Raised with the index of the chosen segment.</summary>
    public event EventHandler<int>? Selected;

    /// <summary>Builds the control.</summary>
    public Segmented(IEnumerable<string> labels, int selected = 0)
    {
        Background = Tokens.Brushes.NavBed;
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        Padding = new Thickness(Tokens.Space.TrackInset);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Hair };
        var index = 0;
        foreach (var label in labels)
        {
            var i = index++;
            var text = new TextBlock
            {
                Text = label,
                FontFamily = Tokens.Fonts.Sans,
                FontSize = Tokens.Fonts.Small,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = new Border
            {
                Child = text,
                CornerRadius = new CornerRadius(Tokens.Radius.Pill),
                Padding = new Thickness(Tokens.Layout.NavPillPadX, 0),
                Height = Tokens.Layout.NavPillHeight - 2 * Tokens.Space.TrackInset + Tokens.Space.Tight,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Transitions =
                [
                    new BrushTransition { Property = BackgroundProperty, Duration = Tokens.Motion.Quick },
                ],
            };
            pill.PointerPressed += (_, _) => { Select(i); Selected?.Invoke(this, i); };
            pill.PointerEntered += (_, _) => { if (!IsActive(i)) { pill.Background = Tokens.Brushes.NavHover; text.Foreground = Tokens.Brushes.BrandStrong; } };
            pill.PointerExited += (_, _) => { if (!IsActive(i)) { pill.Background = Tokens.Brushes.None; text.Foreground = Tokens.Brushes.Muted; } };
            _segments.Add((pill, text));
            row.Children.Add(pill);
        }

        Child = row;
        Select(selected);
    }

    private int _active = -1;

    private bool IsActive(int i) => _active == i;

    /// <summary>Sets the active segment without raising <see cref="Selected"/>.</summary>
    public void Select(int index)
    {
        _active = index;
        for (var i = 0; i < _segments.Count; i++)
        {
            var active = i == index;
            _segments[i].Pill.Background = active ? Tokens.Brushes.Card : Tokens.Brushes.None;
            _segments[i].Pill.BoxShadow = active ? Tokens.Shadow.NavActive : Tokens.Shadow.None;
            _segments[i].Label.Foreground = active ? Tokens.Brushes.BrandStrong : Tokens.Brushes.Muted;
            _segments[i].Label.FontWeight = active ? FontWeight.Bold : FontWeight.SemiBold;
        }
    }
}

/// <summary>Text fields on the kit's terms: 34px, 12px radius, panel border, brand focus.</summary>
public static class Field
{
    /// <summary>A single-line text field.</summary>
    public static TextBox Text(string placeholder, string? initial = null, bool secret = false)
    {
        var box = new TextBox
        {
            Text = initial ?? string.Empty,
            Watermark = placeholder,
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Base,
            Foreground = Tokens.Brushes.Ink,
            Background = Tokens.Brushes.Card,
            BorderBrush = Tokens.Brushes.PanelBorder,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Card),
            Padding = new Thickness(Tokens.Layout.FieldPadX, 0),
            Height = Tokens.Layout.FieldHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (secret) box.PasswordChar = '•';
        return box;
    }

    /// <summary>A multi-line text field.</summary>
    public static TextBox Multiline(string placeholder, string? initial = null)
    {
        var box = Text(placeholder, initial);
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.Height = Tokens.Layout.FieldTallHeight;
        box.VerticalContentAlignment = VerticalAlignment.Top;
        box.Padding = new Thickness(Tokens.Layout.FieldPadX, Tokens.Space.Snug);
        return box;
    }

    /// <summary>A search field, with a leading glyph.</summary>
    public static TextBox Search(string placeholder)
    {
        var box = Text(placeholder);
        box.InnerLeftContent = new TextBlock
        {
            Text = "⌕",
            FontSize = Tokens.Fonts.Heading,
            Foreground = Tokens.Brushes.Faint,
            Margin = new Thickness(Tokens.Layout.FieldPadX, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return box;
    }
}

/// <summary>A thin gauge for the model download.</summary>
public sealed class Gauge : Control
{
    /// <summary>How far along, 0…1.</summary>
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(Fraction));

    /// <inheritdoc cref="FractionProperty"/>
    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    static Gauge() => AffectsRender<Gauge>(FractionProperty);

    /// <summary>Creates a gauge at the token height.</summary>
    public Gauge() => Height = Tokens.Layout.GaugeHeight;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Tokens.Brushes.Line, null, new RoundedRect(bounds, Tokens.Radius.Pill));
        var fill = bounds.WithWidth(bounds.Width * Math.Clamp(Fraction, 0, 1));
        context.DrawRectangle(Tokens.Brushes.BrandStrong, null, new RoundedRect(fill, Tokens.Radius.Pill));
    }
}

/// <summary>Headline text in the site's voice: Very Vogue, with an italic accent in brand-strong.</summary>
public static class Headline
{
    /// <summary>The upright line. <c>h1</c>.</summary>
    public static TextBlock Line(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Serif,
        FontSize = Tokens.Fonts.Headline,
        FontWeight = FontWeight.Normal,
        LineHeight = Tokens.Fonts.HeadlineLineHeight,
        LetterSpacing = Tokens.Fonts.TitleTracking,
        Foreground = Tokens.Brushes.Ink,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>The italic accent line. <c>h1 .emphasis</c>.</summary>
    public static TextBlock Accent(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.SerifItalic,
        FontStyle = FontStyle.Italic,
        FontSize = Tokens.Fonts.Headline,
        FontWeight = FontWeight.Normal,
        LineHeight = Tokens.Fonts.HeadlineLineHeight,
        Foreground = Tokens.Brushes.BrandStrong,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>The subtitle beneath. <c>.subtitle</c>.</summary>
    public static TextBlock Subtitle(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Sans,
        FontSize = Tokens.Fonts.Subtitle,
        Foreground = Tokens.Brushes.Muted,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = Tokens.Fonts.Subtitle * 1.5,
    };
}

/// <summary>A quiet metadata label in DM Sans, with no tracking.</summary>
public static class MonoLabel
{
    /// <summary>Creates the label.</summary>
    public static TextBlock Make(string text, IBrush? brush = null) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Sans,
        FontSize = Tokens.Fonts.MonoLabel,
        FontWeight = FontWeight.Normal,
        LetterSpacing = Tokens.Fonts.MonoLabelTracking,
        Foreground = brush ?? Tokens.Brushes.Faint,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>
/// A fill-only status chip with a dot and sentence-case label.
/// </summary>
public sealed class Badge : Border
{
    private readonly StatusDot _dot;
    private readonly TextBlock _label;

    /// <summary>Creates a badge.</summary>
    public Badge(string text, IBrush? dot = null)
    {
        _dot = new StatusDot { Fill = dot ?? Tokens.Brushes.Brand, VerticalAlignment = VerticalAlignment.Center };
        _label = MonoLabel.Make(text, Tokens.Brushes.Muted);

        Background = Tokens.Brushes.Surface;
        BorderBrush = Tokens.Brushes.None;
        BorderThickness = new Thickness(Tokens.Border.Hairline);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        BoxShadow = Tokens.Shadow.None;
        Height = Tokens.Layout.BadgeHeight;
        Padding = new Thickness(Tokens.Space.Base, 0, Tokens.Layout.BadgePadX, 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _dot, _label },
        };
    }

    /// <summary>Updates the label and dot.</summary>
    public void Set(string text, IBrush dot, bool live)
    {
        _label.Text = text;
        _dot.Fill = dot;
        _dot.IsLive = live;
    }
}

/// <summary>Quiet navigation with a neutral selected fill and stronger label.</summary>
public sealed class NavLink : Button
{
    private readonly TextBlock _label;
    private readonly Border _surface;
    private bool _active;

    /// <summary>Creates the link.</summary>
    public NavLink(string text)
    {
        _label = new TextBlock
        {
            Text = text,
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Nav,
            FontWeight = FontWeight.Medium,
            Foreground = Tokens.Brushes.Muted,
        };
        _surface = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Control),
            Padding = new Thickness(Tokens.Layout.NavPillPadX, Tokens.Space.Snug),
            Child = _label,
        };
        Background = Tokens.Brushes.None;
        BorderThickness = new Thickness(0);
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        Template = new FuncControlTemplate<NavLink>((_, _) => _surface);
    }

    /// <summary>Whether this link is the current section.</summary>
    public bool IsActive
    {
        get => _active;
        set { _active = value; Paint(); }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty) Paint();
    }

    private void Paint()
    {
        var lit = _active || IsPointerOver;
        _label.Foreground = lit ? Tokens.Brushes.Ink : Tokens.Brushes.Muted;
        _label.FontWeight = _active ? FontWeight.SemiBold : FontWeight.Medium;
        _surface.Background = lit ? Tokens.Brushes.Surface : Tokens.Brushes.None;
    }
}

/// <summary>The wordmark: the slash-dot mark, then the name in Very Vogue.</summary>
public static class Wordmark
{
    /// <summary>Creates the wordmark.</summary>
    public static StackPanel Make(string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = Tokens.Space.Snug,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new LogoMark { VerticalAlignment = VerticalAlignment.Center },
            new TextBlock
            {
                Text = text,
                FontFamily = Tokens.Fonts.Serif,
                FontSize = Tokens.Fonts.Wordmark,
                FontWeight = FontWeight.Normal,
                LetterSpacing = Tokens.Fonts.TitleTracking * (Tokens.Fonts.Wordmark / Tokens.Fonts.Title),
                Foreground = Tokens.Brushes.Ink,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };
}

/// <summary>The white coin that rides inside the hero button. <c>.button-coin</c>.</summary>
public sealed class Coin : Control
{
    /// <summary>Whether the coin shows the record dot or the arrow.</summary>
    public static readonly StyledProperty<bool> IsStopProperty =
        AvaloniaProperty.Register<Coin, bool>(nameof(IsStop));

    /// <inheritdoc cref="IsStopProperty"/>
    public bool IsStop
    {
        get => GetValue(IsStopProperty);
        set => SetValue(IsStopProperty, value);
    }

    static Coin() => AffectsRender<Coin>(IsStopProperty);

    /// <summary>Creates the coin at the token size.</summary>
    public Coin()
    {
        Width = Tokens.Layout.Coin;
        Height = Tokens.Layout.Coin;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = Bounds.Width / 2;
        context.DrawEllipse(Tokens.Brushes.Card, null, c, r, r);

        if (IsStop)
        {
            var s = r * 0.42;
            context.DrawRectangle(Tokens.Brushes.BrandStrong, null, new RoundedRect(new Rect(c.X - s, c.Y - s, 2 * s, 2 * s), Tokens.Radius.Bar));
        }
        else
        {
            context.DrawEllipse(Tokens.Brushes.Rose, null, c, r * 0.32, r * 0.32);
        }
    }
}
