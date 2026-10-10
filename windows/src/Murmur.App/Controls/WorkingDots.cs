using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.App.Design;

namespace Murmur.App.Controls;

/// <summary>
/// The house loader: three small dots in the brand colour, each hopping in turn. Shown wherever
/// Acapella is working on what was said, in place of bars that have nothing to follow.
/// </summary>
/// <remarks>
/// The Bible, Part 2 §4: "loading is three small dots in the client colour, nothing more", and
/// Part 1: "anything working out a suggestion shows it is working". The dots stand still when
/// motion is off (<see cref="Tokens.Motion.Animate"/>).
/// </remarks>
public sealed class WorkingDots : Control
{
    private const int Count = 3;
    private readonly Stopwatch _clock = new();
    private DispatcherTimer? _ticker;

    /// <summary>Creates the loader at the token size.</summary>
    public WorkingDots()
    {
        Width = Count * Tokens.Layout.LoaderDot + (Count - 1) * Tokens.Layout.LoaderGap;
        Height = Tokens.Layout.LoaderDot + Tokens.Layout.LoaderBounce;
    }

    /// <summary>How high dot <paramref name="index"/> stands at <paramref name="seconds"/>, 0 to 1.</summary>
    /// <remarks>A hop for half the round and a rest for the other half, each dot a beat behind the last.</remarks>
    public static double Lift(int index, double seconds)
    {
        var round = seconds / Tokens.Motion.LoaderRound.TotalSeconds - index * Tokens.Motion.LoaderStagger;
        return Math.Max(0, Math.Sin(2 * Math.PI * round));
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!Tokens.Motion.Animate) return;
        _clock.Restart();
        _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = Tokens.Motion.Frame };
        _ticker.Tick += (_, _) => { if (IsEffectivelyVisible) InvalidateVisual(); };
        _ticker.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ticker?.Stop();
        _ticker = null;
        _clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var radius = Tokens.Layout.LoaderDot / 2;
        var seconds = _clock.Elapsed.TotalSeconds;
        for (var i = 0; i < Count; i++)
        {
            var lift = _clock.IsRunning ? Lift(i, seconds) : 0;
            var centre = new Point(
                radius + i * (Tokens.Layout.LoaderDot + Tokens.Layout.LoaderGap),
                Bounds.Height - radius - lift * Tokens.Layout.LoaderBounce);
            context.DrawEllipse(Tokens.Brushes.Brand, null, centre, radius, radius);
        }
    }
}
