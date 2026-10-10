using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Murmur.Abstractions;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Controls;

/// <summary>
/// Line icons on a 24-unit grid, drawn with round caps and joins (the Lucide set Sidgrove
/// Intelligence uses, transcribed as path data so nothing has to ship as a font).
/// </summary>
public static class Icons
{
    /// <summary>A microphone.</summary>
    public const string Mic = "M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z M19 10v2a7 7 0 0 1-14 0v-2 M12 19v3";

    /// <summary>A microphone struck through.</summary>
    public const string MicOff = "M2 2l20 20 M18.89 13.23A7 7 0 0 0 19 12v-2 M5 10v2a7 7 0 0 0 12 5 M15 9.34V5a3 3 0 0 0-5.68-1.33 M9 9v3a3 3 0 0 0 5.12 2.12 M12 19v3";

    /// <summary>An open book.</summary>
    public const string Book = "M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2Z M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7Z";

    /// <summary>Sliders.</summary>
    public const string Sliders = "M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4";

    /// <summary>A keyboard.</summary>
    public const string Keyboard = "M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Z M6 9h.01 M10 9h.01 M14 9h.01 M18 9h.01 M8 13h.01 M12 13h.01 M16 13h.01 M7 16h10";

    /// <summary>A speaker with sound.</summary>
    public const string Volume = "M11 5 6 9H2v6h4l5 4Z M15.54 8.46a5 5 0 0 1 0 7.07 M19.07 4.93a10 10 0 0 1 0 14.14";

    /// <summary>A chip.</summary>
    public const string Cpu = "M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Z M9.5 9.5h5v5h-5Z M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3";

    /// <summary>A pen on a line.</summary>
    public const string Pen = "M12 20h9 M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z";

    /// <summary>Sparkles.</summary>
    public const string Sparkles = "M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9Z M19 3v4 M21 5h-4 M5 17v3 M6.5 18.5h-3";

    /// <summary>A cloud.</summary>
    public const string Cloud = "M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z";

    /// <summary>A lightning bolt.</summary>
    public const string Zap = "M13 2 3 14h9l-1 8 10-12h-9Z";

    /// <summary>A double tick.</summary>
    public const string Learn = "M18 6 7 17l-5-5 M22 10l-7.5 7.5L13 16";

    /// <summary>A switch.</summary>
    public const string Toggle = "M8 5h8a7 7 0 0 1 0 14H8A7 7 0 0 1 8 5Z M13 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0";

    /// <summary>A paper plane.</summary>
    public const string Send = "M22 2 11 13 M22 2l-7 20-4-9-9-4Z";

    /// <summary>Two sheets.</summary>
    public const string Copy = "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2Z M4 16a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2";

    /// <summary>A bin.</summary>
    public const string Trash = "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2";

    /// <summary>An ear.</summary>
    public const string Ear = "M6 8.5a6.5 6.5 0 1 1 13 0c0 6-6 6-6 10a3.5 3.5 0 1 1-7 0 M15 8.5a2.5 2.5 0 0 0-5 0v1a2 2 0 1 1 0 4";

    /// <summary>A magnifier.</summary>
    public const string More = "M5 12h.01 M12 12h.01 M19 12h.01";

    /// <summary>Search.</summary>
    public const string Search = "M4 11a7 7 0 1 0 14 0a7 7 0 1 0-14 0 M21 21l-4.3-4.3";

    /// <summary>A chevron pointing right: the end of a row that opens.</summary>
    public const string ChevronRight = "M9 18l6-6-6-6";

    /// <summary>A plus.</summary>
    public const string Plus = "M12 5v14 M5 12h14";

    /// <summary>A cross.</summary>
    public const string Close = "M18 6 6 18 M6 6l12 12";

    /// <summary>An undo arrow.</summary>
    public const string Undo = "M3 7v6h6 M21 17a9 9 0 0 0-9-9a9 9 0 0 0-6 2.3L3 13";

    /// <summary>A warning triangle.</summary>
    public const string Alert = "M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h16.9a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z M12 9v4 M12 17h.01";

    /// <summary>A tick.</summary>
    public const string Check = "M20 6 9 17l-5-5";

    /// <summary>An open folder.</summary>
    public const string Folder = "M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9l-.8-1.2A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z";

    /// <summary>A download arrow.</summary>
    public const string Download = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3";

    /// <summary>A power button.</summary>
    public const string Power = "M12 2v10 M18.4 6.6a9 9 0 1 1-12.77.04";

    /// <summary>A question in a circle, for "there is more to read here".</summary>
    public const string Help = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3 M12 17h.01";

    /// <summary>A play triangle, for trying something.</summary>
    public const string Play = "M6 3l14 9-14 9Z";

    /// <summary>Two arrows chasing round, for syncing.</summary>
    public const string Refresh = "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16 M16 16h5v5";

    /// <summary>A hand wave, for the welcome.</summary>
    public const string Wave = "M18 11V6a2 2 0 0 0-4 0v5 M14 10V4a2 2 0 0 0-4 0v6 M10 10.5V6a2 2 0 0 0-4 0v8 M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-5.99-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15";

    /// <summary>A file of text.</summary>
    public const string File = "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z M14 2v5h6 M8 13h8 M8 17h5";

    private static readonly Dictionary<string, Geometry> Parsed = [];

    /// <summary>The parsed geometry for <paramref name="data"/>, parsed once.</summary>
    public static Geometry Geometry(string data)
    {
        lock (Parsed)
        {
            if (!Parsed.TryGetValue(data, out var geometry))
            {
                geometry = StreamGeometry.Parse(data);
                Parsed[data] = geometry;
            }
            return geometry;
        }
    }
}

/// <summary>One line icon, drawn in its ink at any size.</summary>
public sealed class Glyph : Control
{
    /// <summary>The path data, one of <see cref="Icons"/>.</summary>
    public static readonly StyledProperty<string?> DataProperty = AvaloniaProperty.Register<Glyph, string?>(nameof(Data));

    /// <summary>The ink.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<Glyph, IBrush?>(nameof(Ink), Tokens.Brushes.Muted);

    /// <summary>The stroke on the 24-unit grid: heavier when small, so it reads.</summary>
    public static readonly StyledProperty<double> WeightProperty = AvaloniaProperty.Register<Glyph, double>(nameof(Weight), 2.2);

    /// <summary>
    /// How much of the line is drawn, 0 to 1, from its start: a tick that draws itself in. 1, the
    /// whole glyph, unless something is animating it.
    /// </summary>
    public static readonly StyledProperty<double> RevealProperty = AvaloniaProperty.Register<Glyph, double>(nameof(Reveal), 1);

    static Glyph() => AffectsRender<Glyph>(DataProperty, InkProperty, WeightProperty, RevealProperty);

    /// <summary>Creates a glyph.</summary>
    public Glyph(string? data = null, double size = 14, IBrush? ink = null)
    {
        Data = data;
        Width = size;
        Height = size;
        if (ink is not null) Ink = ink;
        Weight = size <= 13 ? 2.4 : 2.2;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <inheritdoc cref="DataProperty"/>
    public string? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    /// <inheritdoc cref="InkProperty"/>
    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }

    /// <inheritdoc cref="WeightProperty"/>
    public double Weight { get => GetValue(WeightProperty); set => SetValue(WeightProperty, value); }

    /// <inheritdoc cref="RevealProperty"/>
    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Data is not { Length: > 0 } data || Ink is not { } ink || Bounds.Width <= 0 || Reveal <= 0) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        var geometry = Icons.Geometry(data);
        // Part drawn: one dash as long as the part, then a gap longer than the rest (in pen widths).
        DashStyle? dash = null;
        if (Reveal < 1)
        {
            var length = geometry.ContourLength / Weight;
            dash = new DashStyle([length * Reveal, length + 1], 0);
        }
        var pen = new Pen(ink, Weight, dash, PenLineCap.Round, PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }
}

/// <summary>
/// The small coloured square that carries an icon beside a label. Fill only, no border
/// (Dave, 23/09/2026: "less harshness"). <c>IconTile.tsx</c>.
/// </summary>
public sealed class IconTile : Border
{
    private readonly Glyph _glyph;

    /// <summary>Creates a tile in <paramref name="accent"/> at one of the ramp's sizes.</summary>
    public IconTile(string icon, Tokens.Accent accent, double size = Tokens.Layout.Tile, bool round = false)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(round ? size / 2 : size <= Tokens.Layout.TileSmall ? Tokens.Radius.Control - 2 : Tokens.Radius.Button);
        VerticalAlignment = VerticalAlignment.Center;
        _glyph = new Glyph(icon, GlyphSize(size)) { HorizontalAlignment = HorizontalAlignment.Center };
        Child = _glyph;
        SetAccent(accent);
        // A state that changes hue (ready, listening, tidying up) melts from one to the next
        // rather than snapping. Set after the first colour, so a tile is born in its hue.
        if (Tokens.Motion.Animate)
        {
            Transitions = [new BrushTransition { Property = BackgroundProperty, Duration = Tokens.Motion.Quick }];
            _glyph.Transitions = [new BrushTransition { Property = Glyph.InkProperty, Duration = Tokens.Motion.Quick }];
        }
    }

    /// <summary>The tile's hue.</summary>
    public Tokens.Accent Accent { get; private set; } = Tokens.Accent.Brand;

    /// <summary>The tile's icon, one of <see cref="Icons"/>.</summary>
    public string? Icon => _glyph.Data;

    /// <summary>Recolours the tile, for a state that changes (listening, paused).</summary>
    /// <param name="accent">The new hue.</param>
    /// <param name="melt">False to arrive in the hue at once: a tile nobody was looking at has nothing to melt from.</param>
    public void SetAccent(Tokens.Accent accent, bool melt = true)
    {
        Accent = accent;
        var (tile, glyph) = (Transitions, _glyph.Transitions);
        if (!melt) (Transitions, _glyph.Transitions) = (null, null);
        Background = accent.Fill;
        _glyph.Ink = accent.Ink;
        if (!melt) (Transitions, _glyph.Transitions) = (tile, glyph);
    }

    /// <summary>Changes the icon.</summary>
    public void SetIcon(string icon) => _glyph.Data = icon;

    /// <summary>How much of the icon's line is drawn, 0 to 1: a tick drawing itself in.</summary>
    public double Reveal
    {
        get => _glyph.Reveal;
        set => _glyph.Reveal = value;
    }

    /// <summary>
    /// A small spring as the tile changes state: it starts a little under its size and settles
    /// home, the toggle thumb's spring (Dave, 05/10/2026). Nothing when motion is off.
    /// </summary>
    public void Pop()
    {
        if (!Tokens.Motion.Animate) return;
        RenderTransform ??= new ScaleTransform(1, 1);
        _ = PopAnimation.RunAsync(this);
    }

    private static readonly Animation PopAnimation = new()
    {
        Duration = Tokens.Motion.Glide,
        Easing = Thumb.Spring,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters = { new Setter(ScaleTransform.ScaleXProperty, Tokens.Motion.TilePop), new Setter(ScaleTransform.ScaleYProperty, Tokens.Motion.TilePop) },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter(ScaleTransform.ScaleXProperty, 1d), new Setter(ScaleTransform.ScaleYProperty, 1d) },
            },
        },
    };

    /// <summary>About half the tile, never re-typed at a call site. <c>ICON_TILE_GLYPH</c>.</summary>
    private static double GlyphSize(double tile) => tile switch
    {
        <= Tokens.Layout.TileSmall => 11,
        <= Tokens.Layout.TileRow => 13,
        <= Tokens.Layout.Tile => 15,
        _ => 17,
    };
}

/// <summary>
/// A 22px chip: a count, a tag, a state. Light fill and the same hue's ink, a declared
/// height, never wraps. <c>CHIP_BASE</c>.
/// </summary>
public sealed class Chip : Border
{
    private readonly TextBlock _label;

    /// <summary>Creates a chip, optionally led by a small glyph.</summary>
    public Chip(string text, Tokens.Accent accent, string? icon = null)
    {
        Height = Tokens.Layout.ChipHeight;
        CornerRadius = new CornerRadius(Tokens.Radius.Button);
        Padding = new Thickness(icon is null ? Tokens.Layout.ChipPadX : Tokens.Space.Chip, 0, Tokens.Layout.ChipPadX, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Background = accent.Fill;
        _label = new TextBlock
        {
            Text = text,
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Badge,
            FontWeight = FontWeight.SemiBold,
            Foreground = accent.Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Child = icon is null
            ? _label
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Tokens.Space.Tight,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { new Glyph(icon, 11, accent.Ink), _label },
            };
    }

    /// <summary>The chip's words.</summary>
    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }
}

/// <summary>A shortcut drawn as keycaps: Win + Left Ctrl reads as two keys, not a sentence.</summary>
public static class KeyCaps
{
    /// <summary>A row of caps for a description like "Win + Left Ctrl".</summary>
    public static StackPanel Make(string shortcut)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Tight, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in shortcut.Split(" + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            row.Children.Add(new Border
            {
                Height = Tokens.Layout.KeyCapHeight,
                MinWidth = Tokens.Layout.KeyCapHeight,
                Padding = new Thickness(Tokens.Space.Snug, 0),
                CornerRadius = new CornerRadius(Tokens.Radius.Control - 2),
                Background = Tokens.Brushes.Card,
                BorderBrush = Tokens.Brushes.CardBorder,
                BorderThickness = new Thickness(Tokens.Border.Hairline, Tokens.Border.Hairline, Tokens.Border.Hairline, Tokens.Border.Hairline * 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = key,
                    FontFamily = Tokens.Fonts.Sans,
                    FontSize = Tokens.Fonts.Small,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Tokens.Brushes.Ink,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        return row;
    }
}

/// <summary>
/// The mark of the app a dictation went into: its real icon where Windows will give it up,
/// framed like any third-party logo, otherwise its initial on a tile of its own hue. A grey
/// initial is the last resort (the Bible: real marks, not placeholders).
/// </summary>
public sealed class AppMark : Border
{
    private static readonly Dictionary<string, Bitmap?> Read = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IAppIcons?> Reader = new(PlatformFactory.CreateAppIcons);

    /// <summary>Where read icons are kept, so an app shows its mark even when it is closed.</summary>
    public static string CacheFolder { get; set; } = Path.Combine(AppPaths.Root, "app-icons");

    /// <summary>The pixel size icons are read and kept at: crisp at a 32px mark on a 150% screen.</summary>
    public const int IconPixels = 64;

    /// <summary>Creates the mark for <paramref name="app"/>, the process name the history records.</summary>
    public AppMark(string app, double size = Tokens.Layout.Tile)
    {
        Width = size;
        Height = size;
        VerticalAlignment = VerticalAlignment.Top;
        CornerRadius = new CornerRadius(Tokens.Radius.Button);
        ToolTip.SetTip(this, DisplayName(app));

        if (Icon(app) is { } bitmap)
        {
            Background = Tokens.Brushes.Card;
            BorderBrush = Tokens.Brushes.MarkEdge;
            BorderThickness = new Thickness(Tokens.Border.Hairline);
            Padding = new Thickness(Math.Round(size * 0.14));
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
            Child = image;
            return;
        }

        var accent = AccentFor(app);
        Background = accent.Fill;
        Child = new TextBlock
        {
            Text = DisplayName(app)[..1].ToUpperInvariant(),
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Body,
            FontWeight = FontWeight.Bold,
            Foreground = accent.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>The name a person would use for an app the history knows by its process name.</summary>
    public static string DisplayName(string app) => app.ToLowerInvariant() switch
    {
        "claude" => "Claude",
        "chatgpt" => "ChatGPT",
        "codex" => "Codex",
        "slack" => "Slack",
        "outlook" or "olk" => "Outlook",
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "firefox" => "Firefox",
        "ms-teams" or "teams" => "Teams",
        "winword" => "Word",
        "excel" => "Excel",
        "powerpnt" => "PowerPoint",
        "onenote" => "OneNote",
        "code" => "VS Code",
        "cursor" => "Cursor",
        "windowsterminal" => "Terminal",
        "notepad" => "Notepad",
        "notion" => "Notion",
        "whatsapp" => "WhatsApp",
        "explorer" => "File Explorer",
        _ => app.Length == 0 ? "?" : char.ToUpperInvariant(app[0]) + app[1..],
    };

    /// <summary>The hue an app without an icon wears: always the same one for the same app.</summary>
    public static Tokens.Accent AccentFor(string app)
    {
        var hash = 0;
        foreach (var c in app.ToLowerInvariant()) hash = unchecked(hash * 31 + c);
        var cycle = Tokens.Accent.Cycle;
        return cycle[(int)((uint)hash % (uint)(cycle.Count - 1))];
    }

    private static Bitmap? Icon(string app)
    {
        lock (Read)
        {
            if (Read.TryGetValue(app, out var known)) return known;
            var bitmap = FromCache(app) ?? FromProcess(app);
            Read[app] = bitmap;
            return bitmap;
        }
    }

    private static string CachePath(string app) => Path.Combine(CacheFolder, $"{string.Concat(app.ToLowerInvariant().Where(char.IsLetterOrDigit))}.png");

    private static Bitmap? FromCache(string app)
    {
        try
        {
            var path = CachePath(app);
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static WriteableBitmap? FromProcess(string app)
    {
        if (Reader.Value?.Find(app, IconPixels) is not { } icon) return null;
        try
        {
            var bitmap = new WriteableBitmap(new PixelSize(icon.Width, icon.Height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using (var buffer = bitmap.Lock())
            {
                for (var row = 0; row < icon.Height; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(icon.Bgra, row * icon.Width * 4, buffer.Address + row * buffer.RowBytes, icon.Width * 4);
                }
            }
            try
            {
                Directory.CreateDirectory(CacheFolder);
                bitmap.Save(CachePath(app));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"could not keep the icon for {app}: {e.Message}");
            }
            return bitmap;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>A card's header: its tinted icon tile, its name, and anything that belongs on the same row.</summary>
public static class SectionHead
{
    /// <summary>Creates the header.</summary>
    /// <param name="icon">One of <see cref="Icons"/>.</param>
    /// <param name="accent">The section's own hue.</param>
    /// <param name="title">Sentence case, a few words.</param>
    /// <param name="tip">What the section is for, for anyone who wants it; never a line under the title.</param>
    /// <param name="trailing">Chips or an action, on the title row's right.</param>
    public static DockPanel Make(string icon, Tokens.Accent accent, string title, string? tip = null, Control? trailing = null)
    {
        var name = new TextBlock
        {
            Text = title,
            FontFamily = Tokens.Fonts.Sans,
            FontSize = Tokens.Fonts.Heading,
            FontWeight = FontWeight.Bold,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Base, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new IconTile(icon, accent));
        left.Children.Add(name);
        if (tip is not null) left.Children.Add(Hint.Make(tip));

        var row = new DockPanel { LastChildFill = true };
        if (trailing is not null)
        {
            DockPanel.SetDock(trailing, Dock.Right);
            trailing.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(trailing);
        }
        row.Children.Add(left);
        return row;
    }
}

/// <summary>A small question mark that carries the explanation in its tooltip, so the page doesn't have to.</summary>
public static class Hint
{
    /// <summary>Creates the hint. A transparent fill, so the whole square answers the pointer.</summary>
    public static Border Make(string tip)
    {
        var hint = new Border
        {
            Background = Tokens.Brushes.None,
            Padding = new Thickness(Tokens.Space.Hair),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Help),
            Child = new Glyph(Icons.Help, 14, Tokens.Brushes.Faint),
        };
        ToolTip.SetTip(hint, tip);
        ToolTip.SetShowDelay(hint, 150);
        return hint;
    }
}
