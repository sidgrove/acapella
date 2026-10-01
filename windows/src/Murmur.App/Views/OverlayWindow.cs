using System.Diagnostics;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.Abstractions;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// The pill shown over other applications while you dictate.
/// </summary>
/// <remarks>
/// <para>
/// It appears on the monitor holding the window the text is going into, found through
/// <see cref="IWindowTweaks.ActiveWindowCentre"/> — not on the primary monitor, which is
/// where the first version put it.
/// </para>
/// <para>
/// <b>It must never take focus.</b> <c>ShowActivated</c> is off, it is not hit-testable,
/// and on Windows <see cref="IWindowTweaks.MakeNonActivating"/> sets <c>WS_EX_NOACTIVATE</c>.
/// </para>
/// <para>
/// While recording it shows the tail of the running transcript, so words appear as they
/// are spoken. That is the single biggest thing that makes dictation feel responsive.
/// </para>
/// </remarks>
public sealed class OverlayWindow : Window
{
    private readonly IWindowTweaks? _tweaks;
    private readonly StatusDot _dot;
    private readonly LevelBars _bars;
    private readonly TextBlock _state;
    private readonly TextBlock _counter;
    private readonly TextBlock _preview;
    private readonly ScrollViewer _previewScroll;
    private readonly Border _panel;
    private bool _scrollPreviewToEnd;
    private readonly SendPulse _sendPulse;
    private readonly DispatcherTimer _sendTimer = new() { Interval = Tokens.Motion.Frame };
    private readonly Stopwatch _sendClock = new();
    private readonly DispatcherTimer _noticeTimer = new() { Interval = Tokens.Motion.DroppedNotice };

    /// <summary>Whether the short auto-send confirmation is being displayed.</summary>
    public bool IsShowingSendFeedback => _sendTimer.IsEnabled;

    /// <summary>Whether a brief "nothing was typed" notice is being displayed.</summary>
    public bool IsShowingNotice => _noticeTimer.IsEnabled;

    /// <summary>Whether the pill is busy with something the state sync must not cut short.</summary>
    public bool IsShowingTransient => IsShowingSendFeedback || IsShowingNotice;

    /// <summary>Builds the overlay. Not shown until <see cref="Present"/>.</summary>
    public OverlayWindow(IWindowTweaks? tweaks)
    {
        _tweaks = tweaks;

        Width = Tokens.Layout.OverlayWidth + Tokens.Layout.OverlayShadowRoom * 2;
        Height = Tokens.Layout.OverlayHeight + Tokens.Layout.OverlayShadowRoom * 2;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None];
        FontFamily = Tokens.Fonts.Sans;

        _dot = new StatusDot { Fill = Tokens.Accent.Crimson.Ink, IsLive = true, VerticalAlignment = VerticalAlignment.Center };
        _bars = new LevelBars(Tokens.Layout.BarsCount, Tokens.Layout.OverlayBarsHeight) { VerticalAlignment = VerticalAlignment.Center };
        _state = Text.BodyStrong("Listening");
        _state.TextWrapping = TextWrapping.NoWrap;
        _state.VerticalAlignment = VerticalAlignment.Center;
        _counter = Text.Number("00:00", Tokens.Fonts.Body, Tokens.Brushes.Muted);
        _counter.VerticalAlignment = VerticalAlignment.Center;
        _preview = Text.Body(string.Empty);
        _preview.TextWrapping = TextWrapping.Wrap;
        _preview.TextTrimming = TextTrimming.None;
        _preview.MaxWidth = Tokens.Layout.OverlayPreviewWidth;
        _preview.VerticalAlignment = VerticalAlignment.Top;
        _preview.IsVisible = false;

        _previewScroll = new ScrollViewer
        {
            Content = _preview,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        };
        _previewScroll.LayoutUpdated += (_, _) =>
        {
            if (!_scrollPreviewToEnd) return;
            _scrollPreviewToEnd = false;
            _previewScroll.ScrollToEnd();
        };
        var contents = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var header = Panels.Split(Panels.Row(Tokens.Space.Snug, _dot, _state), _counter);
        _bars.HorizontalAlignment = HorizontalAlignment.Center;
        _bars.Margin = new Thickness(0, Tokens.Space.Snug);
        Grid.SetRow(_bars, 1);
        Grid.SetRow(_previewScroll, 2);
        contents.Children.Add(header);
        contents.Children.Add(_bars);
        contents.Children.Add(_previewScroll);
        _sendPulse = new SendPulse
        {
            Width = Tokens.SendFeedback.Width, Height = Tokens.SendFeedback.Height,
            HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false,
            Margin = new Thickness(0, Tokens.Space.Snug),
        };
        Grid.SetRow(_sendPulse, 1);
        contents.Children.Add(_sendPulse);
        _sendTimer.Tick += (_, _) =>
        {
            _sendPulse.Elapsed = _sendClock.Elapsed.TotalSeconds;
            _sendPulse.InvalidateVisual();
            if (_sendClock.Elapsed >= Tokens.SendFeedback.Duration)
            {
                _sendTimer.Stop();
                _sendClock.Stop();
                Hide();
            }
        };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            Hide();
        };
        Closed += (_, _) => { _sendTimer.Stop(); _noticeTimer.Stop(); };

        _panel = new Border
        {
            Background = Tokens.Brushes.Card,
            BorderBrush = Tokens.Brushes.CardBorder,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Frame),
            BoxShadow = Tokens.Shadow.Lift,
            Height = Tokens.Layout.OverlayHeight,
            Padding = new Thickness(Tokens.Space.Base),
            Margin = new Thickness(Tokens.Layout.OverlayShadowRoom),
            VerticalAlignment = VerticalAlignment.Center,
            Child = contents,
        };

        Content = _panel;

        Opened += (_, _) => tweaks?.MakeNonActivating(TryGetPlatformHandle()?.Handle ?? 0);
    }

    /// <summary>Shows the pill at the bottom centre of the screen the user is working on.</summary>
    public void Present()
    {
        var anchor = _tweaks?.ActiveWindowCentre();
        var screen = anchor is { } a ? Screens.ScreenFromPoint(new PixelPoint(a.X, a.Y)) : null;
        screen ??= Screens.Primary;

        if (screen is not null)
        {
            var work = screen.WorkingArea;
            var scale = screen.Scaling;
            var width = (Bounds.Width > 0 ? Bounds.Width : Tokens.Layout.OverlayWidth + Tokens.Layout.OverlayShadowRoom * 2) * scale;
            Position = new PixelPoint(
                (int)(work.X + (work.Width - width) / 2),
                (int)(work.Bottom - (Height + Tokens.Layout.OverlayBottomMargin - Tokens.Layout.OverlayShadowRoom) * scale));
        }

        if (!IsVisible)
        {
            Show();
            Log.Info($"overlay shown on {screen?.DisplayName ?? "no screen"} at {Position.X},{Position.Y}");
        }

        // The style bit alone is not enough: see IWindowTweaks.KeepOnTop.
        _tweaks?.KeepOnTop(TryGetPlatformHandle()?.Handle ?? 0);
    }

    /// <summary>Pushes the current state onto the pill.</summary>
    /// <param name="recording">The key is down.</param>
    /// <param name="transcribing">The key is up and the model is working.</param>
    /// <param name="cleaning">The AI tier is on, so the wait after transcribing is the network.</param>
    /// <param name="level">Input level, 0…1.</param>
    /// <param name="counter">Elapsed time, formatted.</param>
    /// <param name="preview">The running transcript, or empty.</param>
    public void Sync(bool recording, bool transcribing, bool cleaning, double level, string counter, string preview)
    {
        if (IsShowingTransient && !recording) return;
        _sendTimer.Stop();
        _sendClock.Stop();
        _noticeTimer.Stop();
        _sendPulse.IsVisible = false;
        _bars.IsVisible = true;
        _dot.IsVisible = true;
        _bars.IsLive = recording;
        _bars.Level = Math.Clamp(level * Tokens.Layout.OverlayLevelGain, 0, 1);
        // The same words and hues as the status card: crimson while it listens, amber while it works.
        _dot.Fill = recording ? Tokens.Accent.Crimson.Ink : Tokens.Brushes.AmberMid;
        _state.Text = recording ? "Listening" : cleaning ? "Tidying up" : "Writing it out";
        _counter.Text = counter;

        var tail = Tail(preview);
        if (_preview.Text != tail)
        {
            _preview.Text = tail;
            _scrollPreviewToEnd = true;
        }
        _preview.IsVisible = tail.Length > 0;

        _previewScroll.IsVisible = _preview.IsVisible;
        _preview.Measure(new Size(Tokens.Layout.OverlayPreviewWidth, double.PositiveInfinity));
        var extra = _preview.IsVisible
            ? Math.Min(Tokens.Layout.OverlayTextHeight, _preview.DesiredSize.Height + Tokens.Space.Snug)
            : 0;
        var panelHeight = Tokens.Layout.OverlayHeight + extra;
        if (_panel.Height != panelHeight)
        {
            _panel.Height = panelHeight;
            Height = panelHeight + Tokens.Layout.OverlayShadowRoom * 2;
            if (IsVisible) Present();
        }
    }

    /// <summary>
    /// Says briefly why nothing was typed — "Nothing heard" — then goes away.
    /// </summary>
    /// <remarks>
    /// A pill that simply vanishes after a recording reads as the app having failed. A
    /// second of explanation is the difference between "it dropped my words" and "I
    /// didn't say anything it could use".
    /// </remarks>
    public void ShowNotice(string text)
    {
        _sendTimer.Stop();
        _sendClock.Stop();
        _sendPulse.IsVisible = false;
        _state.Text = text;
        _counter.Text = string.Empty;
        _dot.IsVisible = true;
        _dot.Fill = Tokens.Brushes.Muted;
        _bars.IsLive = false;
        _bars.IsVisible = false;
        _previewScroll.IsVisible = false;
        _panel.Height = Tokens.Layout.OverlayHeight;
        Height = Tokens.Layout.OverlayHeight + Tokens.Layout.OverlayShadowRoom * 2;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        Present();
    }

    /// <summary>Shows a brief colour wave after Enter has been delivered, without taking focus.</summary>
    public void ShowSendFeedback()
    {
        _noticeTimer.Stop();
        _state.Text = "Sent ↗";
        _counter.Text = string.Empty;
        _dot.IsVisible = false;
        _bars.IsLive = false;
        _bars.IsVisible = false;
        _previewScroll.IsVisible = false;
        _sendPulse.IsVisible = true;
        _sendPulse.Elapsed = 0;
        _panel.Height = Tokens.Layout.OverlayHeight;
        Height = Tokens.Layout.OverlayHeight + Tokens.Layout.OverlayShadowRoom * 2;
        _sendClock.Restart();
        _sendTimer.Start();
        Present();
    }

    /// <summary>The last few words, so the newest speech is always in view.</summary>
    public static string Tail(string text)
    {
        var flat = text.Replace('\n', ' ').Trim();
        if (flat.Length <= Tokens.Layout.OverlayPreviewChars) return flat;

        var cut = flat[^Tokens.Layout.OverlayPreviewChars..];
        var space = cut.IndexOf(' ', StringComparison.Ordinal);
        return "…" + (space > 0 ? cut[(space + 1)..] : cut);
    }
}
