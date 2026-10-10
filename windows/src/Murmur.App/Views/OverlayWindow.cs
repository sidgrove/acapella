using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
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
/// <para>
/// <b>One row, in the status card's own language</b> (Dave, 10/10/2026: "more joy" in "the popup
/// I get when I activate speech"; the Sidgrove Bible, Part 2 §2: hue lives in the icon tile). A
/// tile whose hue is the state, the state in a word or two, and one thing beside it that moves:
/// the voice bars while it listens, the house loader's three dots while it works, a tick that
/// draws itself in when the words have landed. It was a 100px white box with a dot, a floating
/// word and bars that sat still while the model worked. What went, on purpose: the timer while
/// it works (nobody acts on it), the arrow typed after "Sent" (the tile's paper plane says it)
/// and the empty box under "Nothing heard".
/// </para>
/// </remarks>
public sealed class OverlayWindow : Window
{
    private enum Transient { None, Sent, Notice, Done }

    private readonly IWindowTweaks? _tweaks;
    private readonly IconTile _tile;
    private readonly LevelBars _bars;
    private readonly WorkingDots _dots;
    private readonly SendPulse _sendPulse;
    private readonly TextBlock _state;
    private readonly TextBlock _counter;
    private readonly TextBlock _preview;
    private readonly ScrollViewer _previewScroll;
    private readonly Border _panel;
    private bool _scrollPreviewToEnd;
    private Transient _transient;
    private TimeSpan _transientFor;
    private bool _arrived;
    private bool _leaving;
    private bool _fresh = true;
    private string _face = string.Empty;
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _frame = new() { Interval = Tokens.Motion.Frame };
    private readonly DispatcherTimer _hide = new() { Interval = Tokens.Motion.OverlayOut };

    private static readonly TransformOperations Home = TransformOperations.Parse("translateY(0px)");
    private static readonly TransformOperations Away = TransformOperations.Parse(
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"translateY({Tokens.Layout.OverlayTravel}px)"));

    /// <summary>Whether the short auto-send confirmation is being displayed.</summary>
    public bool IsShowingSendFeedback => _transient == Transient.Sent;

    /// <summary>Whether a brief "nothing was typed" notice is being displayed.</summary>
    public bool IsShowingNotice => _transient == Transient.Notice;

    /// <summary>Whether the brief "Done" tick is being displayed.</summary>
    public bool IsShowingDone => _transient == Transient.Done;

    /// <summary>Whether the pill is busy with something the state sync must not cut short.</summary>
    public bool IsShowingTransient => _transient != Transient.None;

    /// <summary>Whether the pill is fading out on its way to hidden.</summary>
    public bool IsLeaving => _leaving;

    /// <summary>The tile that carries the state's hue and mark. Exposed for headless tests.</summary>
    public IconTile StateTile => _tile;

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

        _tile = new IconTile(Icons.Mic, Tokens.Accent.Crimson);
        _state = Text.BodyStrong("Listening");
        _state.TextWrapping = TextWrapping.NoWrap;
        _state.TextTrimming = TextTrimming.CharacterEllipsis;
        _state.VerticalAlignment = VerticalAlignment.Center;
        _state.Margin = new Thickness(Tokens.Space.Base, 0, 0, 0);
        _counter = Text.Number("00:00", Tokens.Fonts.Small, Tokens.Brushes.Muted);
        _counter.VerticalAlignment = VerticalAlignment.Center;
        _counter.Margin = new Thickness(Tokens.Space.Base, 0, Tokens.Space.Tight, 0);

        // One slot beside the word, and one thing in it at a time.
        _bars = new LevelBars(Tokens.Layout.OverlayBars, Tokens.Layout.OverlayBarsHeight) { VerticalAlignment = VerticalAlignment.Center };
        _dots = new WorkingDots { VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new Thickness(0, 0, Tokens.Space.Tight, 0) };
        _sendPulse = new SendPulse
        {
            Width = Tokens.SendFeedback.Width, Height = Tokens.SendFeedback.Height,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false,
            Margin = new Thickness(0, 0, Tokens.Space.Tight, 0),
        };
        var slot = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Tokens.Space.Base, 0, 0, 0),
            Children = { _bars, _dots, _sendPulse },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Height = Tokens.Layout.Tile };
        Grid.SetColumn(_state, 1);
        Grid.SetColumn(slot, 2);
        Grid.SetColumn(_counter, 3);
        row.Children.Add(_tile);
        row.Children.Add(_state);
        row.Children.Add(slot);
        row.Children.Add(_counter);

        _preview = Text.Body(string.Empty);
        _preview.TextWrapping = TextWrapping.Wrap;
        _preview.TextTrimming = TextTrimming.None;
        _preview.LineHeight = Tokens.Layout.OverlayLineHeight;
        _preview.Width = Tokens.Layout.OverlayPreviewWidth;
        _preview.HorizontalAlignment = HorizontalAlignment.Left;
        _preview.VerticalAlignment = VerticalAlignment.Top;
        _preview.IsVisible = false;

        _previewScroll = new ScrollViewer
        {
            Content = _preview,
            // The words hang under the state word; the tile stands clear, as a mark does on a history row.
            Margin = new Thickness(Tokens.Layout.Tile + Tokens.Space.Base, Tokens.Space.Snug, 0, 0),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            IsVisible = false,
        };
        _previewScroll.LayoutUpdated += (_, _) =>
        {
            if (!_scrollPreviewToEnd) return;
            _scrollPreviewToEnd = false;
            _previewScroll.ScrollToEnd();
        };

        var contents = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(_previewScroll, 1);
        contents.Children.Add(row);
        contents.Children.Add(_previewScroll);

        _frame.Tick += (_, _) => Step();
        _hide.Tick += (_, _) => Leave();
        Closed += (_, _) => { _frame.Stop(); _hide.Stop(); };

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
        if (Tokens.Motion.Animate)
        {
            // Born away, so the first showing rises in like every later one.
            _panel.Opacity = 0;
            _panel.RenderTransform = Away;
        }

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
            _fresh = true;
            Log.Info($"overlay shown on {screen?.DisplayName ?? "no screen"} at {Position.X},{Position.Y}");
        }
        Arrive();

        // The style bit alone is not enough: see IWindowTweaks.KeepOnTop.
        _tweaks?.KeepOnTop(TryGetPlatformHandle()?.Handle ?? 0);
    }

    /// <summary>
    /// Sends the pill away: a short sink and fade, then hidden. Straight to hidden when motion
    /// is off. Asking again while it is already going changes nothing.
    /// </summary>
    public void Dismiss()
    {
        EndTransient();
        if (!IsVisible || _leaving) return;
        if (!Tokens.Motion.Animate)
        {
            Leave();
            return;
        }
        _leaving = true;
        _arrived = false;
        SetPace(Tokens.Motion.OverlayOut);
        _panel.Opacity = 0;
        _panel.RenderTransform = Away;
        _hide.Start();
    }

    private void Leave()
    {
        _hide.Stop();
        _leaving = false;
        _arrived = false;
        Hide();
    }

    private void Arrive()
    {
        _hide.Stop();
        _leaving = false;
        if (_arrived) return;
        _arrived = true;
        if (!Tokens.Motion.Animate) return;
        SetPace(Tokens.Motion.OverlayIn);
        _panel.Opacity = 1;
        _panel.RenderTransform = Home;
    }

    private void SetPace(TimeSpan duration) => _panel.Transitions =
    [
        new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = new CubicEaseOut() },
        new TransformOperationsTransition { Property = RenderTransformProperty, Duration = duration, Easing = new CubicEaseOut() },
    ];

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
        EndTransient();

        // The same words, marks and hues as the status card: crimson while it listens, amber while it works.
        if (recording) Face("Listening", Icons.Mic, Tokens.Accent.Crimson);
        else Face(cleaning ? "Tidying up" : "Writing it out", Icons.Sparkles, Tokens.Accent.Amber);

        _bars.IsVisible = recording;
        _bars.IsLive = recording;
        _bars.Level = Math.Clamp(level * Tokens.Layout.OverlayLevelGain, 0, 1);
        _dots.IsVisible = !recording;
        _sendPulse.IsVisible = false;
        // How long you have been talking is worth a glance; how long the model has been at it is not.
        _counter.IsVisible = recording;
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
        Fit(_preview.IsVisible
            ? Tokens.Space.Snug + Math.Min(Tokens.Layout.OverlayTextHeight, _preview.DesiredSize.Height)
            : 0);
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
        Face(text, Icons.MicOff, Tokens.Accent.Slate);
        Brief(Transient.Notice, Tokens.Motion.DroppedNotice);
    }

    /// <summary>Shows a brief colour wave after Enter has been delivered, without taking focus.</summary>
    public void ShowSendFeedback()
    {
        Face("Sent", Icons.Send, Tokens.Accent.Emerald);
        Brief(Transient.Sent, Tokens.SendFeedback.Duration);
        _sendPulse.Elapsed = 0;
        _sendPulse.IsVisible = true;
    }

    /// <summary>
    /// The words have landed: an emerald tick draws itself in, once and small, and the pill goes.
    /// When the clean-up was set aside and the words went in as heard, it says so in amber
    /// instead: a green tick over that would be a reassurance that isn't true.
    /// </summary>
    /// <param name="asHeard">The AI clean-up failed or was rejected, so the local words were typed.</param>
    public void ShowDone(bool asHeard = false)
    {
        if (asHeard) Face("Typed as heard", Icons.Alert, Tokens.Accent.Amber);
        else Face("Done", Icons.Check, Tokens.Accent.Emerald);
        Brief(Transient.Done, Tokens.Motion.DoneNotice);
        if (!asHeard && Tokens.Motion.Animate) _tile.Reveal = 0;
    }

    /// <summary>The state's tile and word. A change of state the user watched gives the tile its small spring.</summary>
    private void Face(string text, string icon, Tokens.Accent accent)
    {
        // A pill that has only just been shown arrives in its state; there is nothing to melt from.
        var watched = IsVisible && !_fresh && _face.Length > 0;
        _fresh = false;
        _tile.Reveal = 1;
        _state.Text = text;
        var face = $"{icon}|{text}";
        if (face == _face) return;
        _face = face;
        _tile.SetIcon(icon);
        _tile.SetAccent(accent, melt: watched);
        if (watched && !_leaving) _tile.Pop();
    }

    /// <summary>Puts the pill in a state that lasts a moment and then leaves by itself: the row, and nothing under it.</summary>
    private void Brief(Transient kind, TimeSpan duration)
    {
        _transient = kind;
        _transientFor = duration;
        _bars.IsLive = false;
        _bars.IsVisible = false;
        _dots.IsVisible = false;
        _sendPulse.IsVisible = false;
        _counter.IsVisible = false;
        _previewScroll.IsVisible = false;
        Fit(0);
        _clock.Restart();
        _frame.Start();
        Present();
    }

    private void EndTransient()
    {
        _transient = Transient.None;
        _frame.Stop();
        _clock.Stop();
        _tile.Reveal = 1;
    }

    private void Step()
    {
        var elapsed = _clock.Elapsed;
        if (_transient == Transient.Sent)
        {
            _sendPulse.Elapsed = elapsed.TotalSeconds;
            _sendPulse.InvalidateVisual();
        }
        else if (_transient == Transient.Done && _tile.Reveal < 1)
        {
            // Easing out: quick off the mark, settling into the tick's long stroke.
            var t = Math.Clamp(elapsed / Tokens.Motion.TickDraw, 0, 1);
            _tile.Reveal = 1 - Math.Pow(1 - t, 3);
        }
        if (elapsed >= _transientFor) Dismiss();
    }

    private void Fit(double extra)
    {
        var panelHeight = Tokens.Layout.OverlayHeight + extra;
        if (_panel.Height == panelHeight) return;
        _panel.Height = panelHeight;
        Height = panelHeight + Tokens.Layout.OverlayShadowRoom * 2;
        if (IsVisible && _transient == Transient.None) Present();
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
