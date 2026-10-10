using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// A Sidgrove window: the wash background, a slim caption strip with the mark (and, on a sheet,
/// a Very Vogue title) in place of the OS title bar, thin
/// glyphs for the window controls. The strip and the white margin match Setlist's.
/// </summary>
/// <remarks>
/// The client area is extended over the title bar so the whole window is one surface.
/// Dragging the strip moves the window; double-clicking it toggles maximise; Escape closes
/// a sheet.
/// </remarks>
public abstract class ShellWindow : Window
{
    /// <summary>Whether this is a sheet: close only, Escape closes.</summary>
    protected bool IsSheet { get; init; }

    /// <summary>Configures the chrome. Call before setting content.</summary>
    protected ShellWindow()
    {
        Background = Tokens.Brushes.Card;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = Tokens.Layout.CaptionHeight;
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        FontFamily = Tokens.Fonts.Sans;

        // A scrolling list says so softly: the theme's slim bar in the brand's mid tone, not a
        // near-black line on the canvas, firming to the brand under the pointer.
        Resources["ScrollBarPanningThumbBackground"] = Tokens.Brushes.ScrollThumb;
        Resources["ScrollBarThumbFill"] = Tokens.Brushes.ScrollThumb;
        Resources["ScrollBarThumbFillPointerOver"] = Tokens.Brushes.Brand;
        Resources["ScrollBarThumbFillPressed"] = Tokens.Brushes.BrandStrong;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.Alt) return;
            e.Handled = true;
            ShowSystemMenu();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && IsSheet) { Close(); e.Handled = true; }
        };
    }

    /// <summary>Shows the standard keyboard window menu without changing custom chrome.</summary>
    protected virtual void ShowSystemMenu()
    {
        var menu = PlatformFactory.CreateWindowMenu();
        if (menu is null) return;
        var chosen = menu.Show(TryGetPlatformHandle()?.Handle ?? 0);
        Log.Info($"window menu: {(chosen != 0 ? $"command 0x{chosen:X}" : menu.LastError ?? "dismissed")}");
    }

    /// <summary>
    /// Wraps <paramref name="body"/> beneath the caption strip. The strip sits on plain
    /// white; the body sits on the wash inside a rounded, hairline-edged panel with a slim
    /// margin, so the hero is sealed off rather than running to the window edge. A null
    /// <paramref name="title"/> leaves the mark alone top left.
    /// </summary>
    protected Control Frame(string? title, Control body, Control? trailing = null, string? byline = null)
    {
        var panel = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Frame),
            BorderBrush = Tokens.Brushes.Line,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            ClipToBounds = true,
            Margin = new Thickness(Tokens.Layout.PanelInset, 0, Tokens.Layout.PanelInset, Tokens.Layout.PanelInset),
            Child = new WashPanel { Child = body },
        };

        var root = new DockPanel();
        root.Children.Add(Panels.Docked(BuildCaption(title, trailing, byline), Dock.Top));
        root.Children.Add(panel);
        return root;
    }

    private Border BuildCaption(string? title, Control? trailing, string? byline)
    {
        // The site's header: the wordmark alone on the left, nav on the right. It fills what the
        // right leaves, less a little air, so a narrow window trims it rather than overlapping.
        var left = new Wordmark(title, byline) { Margin = new Thickness(0, 0, Tokens.Space.Roomy, 0) };

        var glyphs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Hair,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (!IsSheet)
        {
            var minimise = new CaptionGlyph(CaptionGlyph.Glyph.Minimise);
            minimise.Click += (_, _) => WindowState = WindowState.Minimized;
            var maximise = new CaptionGlyph(CaptionGlyph.Glyph.Maximise);
            maximise.Click += (_, _) => ToggleMaximise();
            glyphs.Children.Add(minimise);
            glyphs.Children.Add(maximise);
        }

        var close = new CaptionGlyph(CaptionGlyph.Glyph.Close);
        close.Click += (_, _) => Close();
        glyphs.Children.Add(close);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Section,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (trailing is not null) right.Children.Add(trailing);
        right.Children.Add(glyphs);
        DockPanel.SetDock(right, Dock.Right);

        var strip = new Border
        {
            Height = Tokens.Layout.CaptionHeight,
            Padding = new Thickness(Tokens.Layout.CaptionPadLeft, 0, Tokens.Space.Base, 0),
            Background = Tokens.Brushes.None,
            Child = new DockPanel { Children = { right, left } },
        };

        strip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(strip).Properties.IsLeftButtonPressed) return;
            if (e.Source is Control source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any())) return;
            if (e.ClickCount == 2 && !IsSheet) ToggleMaximise();
            else BeginMoveDrag(e);
        };

        return strip;
    }

    private void ToggleMaximise() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
