using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.App.Views;
using Shouldly;

namespace Murmur.AppTests;

/// <summary>The listening preview stays compact and shows more than a single line.</summary>
public sealed class OverlayLayoutTests
{
    [AvaloniaFact]
    public void Send_feedback_is_nonactivating_and_new_recording_preempts_it()
    {
        var overlay = new OverlayWindow(null);
        try
        {
            overlay.ShowSendFeedback();
            overlay.IsShowingSendFeedback.ShouldBeTrue();
            overlay.ShowActivated.ShouldBeFalse();
            overlay.IsHitTestVisible.ShouldBeFalse();
            overlay.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Sent").ShouldBeTrue();
            overlay.StateTile.Icon.ShouldBe(Icons.Send, "the paper plane says where it went; no arrow is typed after the word");
            overlay.Sync(true, false, false, 0.5, "00:01", "next recording");
            overlay.IsShowingSendFeedback.ShouldBeFalse();
            overlay.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Listening").ShouldBeTrue();
        }
        finally { overlay.Close(); }
    }

    [AvaloniaFact]
    public void Each_state_wears_its_own_hue_word_and_moving_part()
    {
        var overlay = new OverlayWindow(null);
        try
        {
            overlay.Show();
            var bars = overlay.GetVisualDescendants().OfType<LevelBars>().Single();
            var dots = overlay.GetVisualDescendants().OfType<WorkingDots>().Single();
            var wave = overlay.GetVisualDescendants().OfType<SendPulse>().Single();
            bool Says(string words) => overlay.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == words);

            // Listening: crimson and nothing else is, the bars follow the voice, the time runs.
            overlay.Sync(true, false, false, 0.4, "00:03", "");
            overlay.StateTile.Accent.ShouldBe(Tokens.Accent.Crimson);
            overlay.StateTile.Icon.ShouldBe(Icons.Mic);
            Says("Listening").ShouldBeTrue();
            Says("00:03").ShouldBeTrue();
            (bars.IsVisible && bars.IsLive).ShouldBeTrue();
            dots.IsVisible.ShouldBeFalse();

            // Working: amber, the loader's dots in place of bars with nothing to follow, no timer.
            overlay.Sync(false, true, true, 0, "00:04", "");
            overlay.StateTile.Accent.ShouldBe(Tokens.Accent.Amber);
            overlay.StateTile.Icon.ShouldBe(Icons.Sparkles);
            Says("Tidying up").ShouldBeTrue();
            Says("00:04").ShouldBeFalse("nobody acts on how long the model has been at it");
            dots.IsVisible.ShouldBeTrue();
            bars.IsVisible.ShouldBeFalse();
            overlay.Sync(false, true, false, 0, "00:04", "");
            Says("Writing it out").ShouldBeTrue();

            // Done: an emerald tick, and the engine's next sync must not cut it short.
            overlay.ShowDone();
            overlay.IsShowingDone.ShouldBeTrue();
            overlay.StateTile.Accent.ShouldBe(Tokens.Accent.Emerald);
            overlay.StateTile.Icon.ShouldBe(Icons.Check);
            Says("Done").ShouldBeTrue();
            (bars.IsVisible || dots.IsVisible || wave.IsVisible).ShouldBeFalse();
            overlay.Sync(false, true, true, 0, "00:05", "");
            Says("Done").ShouldBeTrue();

            // Typed as heard is never a green tick.
            overlay.ShowDone(asHeard: true);
            overlay.StateTile.Accent.ShouldBe(Tokens.Accent.Amber);
            overlay.StateTile.Icon.ShouldBe(Icons.Alert);
            Says("Typed as heard").ShouldBeTrue();

            // Nothing heard: slate, the mic struck through.
            overlay.ShowNotice("Nothing heard");
            overlay.IsShowingNotice.ShouldBeTrue();
            overlay.IsShowingDone.ShouldBeFalse();
            overlay.StateTile.Accent.ShouldBe(Tokens.Accent.Slate);
            overlay.StateTile.Icon.ShouldBe(Icons.MicOff);

            // Sent: emerald with the colour wave.
            overlay.ShowSendFeedback();
            overlay.StateTile.Accent.ShouldBe(Tokens.Accent.Emerald);
            wave.IsVisible.ShouldBeTrue();

            // A new recording takes the pill straight back.
            overlay.Sync(true, false, false, 0.4, "00:00", "");
            overlay.IsShowingTransient.ShouldBeFalse();
            overlay.StateTile.Accent.ShouldBe(Tokens.Accent.Crimson);
            wave.IsVisible.ShouldBeFalse();

            overlay.Dismiss();
            overlay.IsVisible.ShouldBeFalse("with motion off the pill goes at once");
        }
        finally { overlay.Close(); }
    }

    [AvaloniaFact]
    public void The_last_word_is_one_row_however_tall_the_pill_had_grown()
    {
        var overlay = new OverlayWindow(null);
        try
        {
            overlay.Show();
            overlay.Sync(true, false, false, 0.4, "00:01", "");
            var row = overlay.Height;
            row.ShouldBe(Tokens.Layout.OverlayHeight + Tokens.Layout.OverlayShadowRoom * 2);
            overlay.Sync(true, false, false, 0.4, "00:09", string.Join(" ", Enumerable.Repeat("words that make the pill grow", 12)));
            overlay.Height.ShouldBe(row + Tokens.Space.Snug + Tokens.Layout.OverlayTextHeight, "four whole lines, never a cut one");
            overlay.ShowDone();
            overlay.Height.ShouldBe(row);
            overlay.Sync(true, false, false, 0.4, "00:02", "more words");
            overlay.ShowNotice("Nothing heard. Is the mic muted?");
            overlay.Height.ShouldBe(row, "no empty box under the notice");
            overlay.UpdateLayout();
            var words = overlay.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Nothing heard. Is the mic muted?");
            var untrimmed = new TextBlock { Text = words.Text, FontFamily = words.FontFamily, FontSize = words.FontSize, FontWeight = words.FontWeight };
            untrimmed.Measure(Avalonia.Size.Infinity);
            words.Bounds.Width.ShouldBeGreaterThanOrEqualTo(untrimmed.DesiredSize.Width - 0.5, "the longest notice fits the row untrimmed");
        }
        finally { overlay.Close(); }
    }

    [AvaloniaFact]
    public void The_done_tick_and_the_tile_never_move_when_motion_is_off()
    {
        Tokens.Motion.Animate.ShouldBeFalse("the headless host runs with motion off");
        var overlay = new OverlayWindow(null);
        try
        {
            overlay.Present();
            overlay.Sync(false, true, false, 0, "00:02", "");
            overlay.ShowDone();
            overlay.StateTile.Reveal.ShouldBe(1, "the tick is whole at once for anyone who asked for less motion");
            overlay.GetVisualDescendants().OfType<Border>().First().Opacity.ShouldBe(1);
            overlay.IsLeaving.ShouldBeFalse();
        }
        finally { overlay.Close(); }
    }

    [Xunit.Fact]
    public void The_loader_dots_hop_in_turn_and_rest()
    {
        var round = Tokens.Motion.LoaderRound.TotalSeconds;
        for (var i = 0; i < 3; i++)
        {
            WorkingDots.Lift(i, (0.25 + i * Tokens.Motion.LoaderStagger) * round).ShouldBe(1, 0.001, $"dot {i} peaks a beat after the one before");
            WorkingDots.Lift(i, (0.75 + i * Tokens.Motion.LoaderStagger) * round).ShouldBe(0, "and rests for the other half of the round");
        }
    }
    [AvaloniaFact]
    public void Presenting_reasserts_always_on_top_every_time()
    {
        var tweaks = new RecordingTweaks();
        var overlay = new OverlayWindow(tweaks);
        try
        {
            overlay.Present();
            tweaks.KeepOnTopCalls.ShouldBe(1, "a shown pill must be pushed back into the topmost band");
            overlay.Present();
            tweaks.KeepOnTopCalls.ShouldBe(2, "a pill that stays shown can still be demoted, so every sync re-asserts");
        }
        finally { overlay.Close(); }
    }

    private sealed class RecordingTweaks : Murmur.Abstractions.IWindowTweaks
    {
        public int KeepOnTopCalls { get; private set; }
        public void MakeNonActivating(nint handle) { }
        public void KeepOnTop(nint handle) => KeepOnTopCalls++;
        public (int X, int Y)? ActiveWindowCentre() => null;
    }

    [AvaloniaFact]
    public void Empty_preview_is_small_and_only_words_expand_it()
    {
        var overlay = new OverlayWindow(null);
        try
        {
            overlay.Sync(true, false, false, 0.5, "00:01", "");
            overlay.Show();
            overlay.UpdateLayout();
            var compact = overlay.Height;
            compact.ShouldBeLessThan(120);
            overlay.Sync(true, false, false, 0.5, "00:02", "These words should reveal the transcript area.");
            overlay.Height.ShouldBeGreaterThan(compact);
            overlay.Sync(true, false, false, 0.5, "00:03", "");
            overlay.Height.ShouldBe(compact);
        }
        finally { overlay.Close(); }
    }

    [AvaloniaFact]
    public void Long_preview_wraps_in_a_compact_nonactivating_window()
    {
        var overlay = new OverlayWindow(null);
        try
        {
            overlay.Sync(true, false, false, 0.1, "00:08", string.Join(" ", Enumerable.Repeat("These are the words being dictated into another application.", 6)));
            overlay.Show();
            overlay.UpdateLayout();
            overlay.UpdateLayout();
            var scroll = overlay.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Offset.Y.ShouldBe(Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height), 0.1, "the newest words must stay in view");
            overlay.Bounds.Width.ShouldBeLessThan(480);
            overlay.ShowActivated.ShouldBeFalse();
            var preview = overlay.GetVisualDescendants().OfType<TextBlock>().MaxBy(t => t.Text?.Length ?? 0)!;
            preview.Text!.Length.ShouldBeGreaterThan(140);
            preview.Bounds.Height.ShouldBeGreaterThan(35);
        }
        finally { overlay.Close(); }
    }
}
