using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.App.Views;
using Shouldly;

[assembly: AvaloniaTestApplication(typeof(Murmur.AppTests.TestAppBuilder))]

namespace Murmur.AppTests;

/// <summary>Hosts the app headlessly so the UI can be exercised without a display.</summary>
public static class TestAppBuilder
{
    /// <summary>Builds a headless Avalonia app for the test host.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        // Real Skia text shaping in the headless host: the headless font manager cannot
        // resolve the bold and semibold cuts of an embedded family, which every heading uses.
        AppBuilder.Configure<TestApp>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>A minimal application shell for headless tests.</summary>
public sealed class TestApp : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        Murmur.App.Design.BundledFonts.Register();
        // The headless clock never runs, so a glide would be captured where it set off from.
        Murmur.App.Design.Tokens.Motion.Animate = false;
        Styles.Add(new FluentTheme());
    }
}

/// <summary>Real UI tests, running with no display.</summary>
public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void Window_opens_and_lays_out()
    {
        var window = new MainWindow();
        window.Show();

        window.Bounds.Width.ShouldBeGreaterThan(0);
        window.Bounds.Height.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void Record_toggles_the_state_and_the_bars_together()
    {
        var window = new MainWindow();
        window.Show();

        window.IsRecording.ShouldBeFalse();
        window.StateText.ShouldBe("Ready");
        window.Bars.IsLive.ShouldBeFalse();

        window.ToggleRecording();

        window.IsRecording.ShouldBeTrue();
        window.StateText.ShouldBe("Listening");
        window.Bars.IsLive.ShouldBeTrue("the bars must follow the transport");

        window.ToggleRecording();

        window.StateText.ShouldBe("Ready");
        window.Bars.IsLive.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Window_honours_its_minimum_size()
    {
        var window = new MainWindow();
        window.Show();

        window.MinWidth.ShouldBe(Tokens.Layout.MainMinWidth);
        window.MinHeight.ShouldBe(Tokens.Layout.MainMinHeight);
    }

    [AvaloniaFact]
    public void A_fault_shows_as_a_notice()
    {
        var window = new MainWindow();
        window.Show();

        window.FaultNotice.IsVisible.ShouldBeFalse();
        window.ReportFault("The microphone could not be opened.");
        window.FaultNotice.IsVisible.ShouldBeTrue();
    }
}

/// <summary>The Sidgrove controls.</summary>
public sealed class ControlTests
{
    [AvaloniaFact]
    public void Level_bars_measure_from_the_token_geometry()
    {
        var bars = new LevelBars(Tokens.Layout.BarsCount, Tokens.Layout.BarsHeight);
        bars.Width.ShouldBe(Tokens.Layout.BarsCount * (Tokens.Layout.BarWidth + Tokens.Layout.BarGap) - Tokens.Layout.BarGap);
        bars.Height.ShouldBe(Tokens.Layout.BarsHeight);
    }

    [AvaloniaFact]
    public void Level_bars_render_live_and_idle()
    {
        var bars = new LevelBars(Tokens.Layout.BarsCountSmall, Tokens.Layout.BarsHeightSmall) { Level = 0.6 };
        var window = new Window { Content = bars };
        window.Show();

        bars.IsLive = true;
        bars.Bounds.Width.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void Buttons_take_the_token_heights()
    {
        new SgButton("Go", SgButton.Kind.Primary).Height.ShouldBe(Tokens.Layout.ButtonHeight);
        new SgButton("Go", SgButton.Kind.Hero).Height.ShouldBe(Tokens.Layout.HeroButtonHeight);
        new SgButton("Go", SgButton.Kind.Quiet, compact: true).Height.ShouldBe(Tokens.Layout.ButtonHeightSmall);
    }

    [AvaloniaFact]
    public void Segmented_control_selects_and_reports()
    {
        var tabs = new Segmented(["One", "Two"]);
        var chosen = -1;
        tabs.Selected += (_, i) => chosen = i;
        tabs.Select(1);
        chosen.ShouldBe(-1, "Select must not raise");
    }

    [AvaloniaFact]
    public void The_toggle_is_the_house_bed_and_its_thumb_sits_on_the_choice()
    {
        Tokens.Colors.ToggleBed.ShouldBe(Avalonia.Media.Color.FromRgb(0xEE, 0xF0, 0xF7), "ONE toggle: #eef0f7 bed, no outline");
        var toggle = new Segmented([new Segmented.Choice("Instant", Icons.Zap, Tokens.Accent.Amber), new Segmented.Choice("Polished", Icons.Sparkles, Tokens.Accent.Plum)]);
        var chosen = -1;
        toggle.Selected += (_, i) => chosen = i;
        var window = new Window { Content = toggle };
        window.Show();
        toggle.BorderThickness.ShouldBe(default, "no outline round the bed");
        toggle.Press(1);
        chosen.ShouldBe(1);
        toggle.SelectedIndex.ShouldBe(1);
        window.UpdateLayout();
        var glide = (GlidePanel)toggle.Child!;
        var second = ((StackPanel)glide.Children[1]).Children[1];
        glide.MarkerX.ShouldBe(second.Bounds.X, 0.5, "the thumb sits under the chosen side");
        glide.MarkerWidth.ShouldBe(second.Bounds.Width, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void Switch_and_dot_take_token_sizes()
    {
        new Switch().Width.ShouldBe(Tokens.Layout.SwitchWidth);
        new StatusDot().Width.ShouldBe(Tokens.Layout.Dot * 2);
    }
}

/// <summary>Guards the brand's rules, which erode one reasonable commit at a time.</summary>
public sealed class DesignSystemTests
{
    [AvaloniaFact]
    public void Brand_blue_is_the_sidgrove_value()
    {
        var brand = Tokens.Colors.Brand;
        brand.R.ShouldBe((byte)0x68);
        brand.G.ShouldBe((byte)0x74);
        brand.B.ShouldBe((byte)0xB4);
    }

    [AvaloniaFact]
    public void Radii_follow_the_crispness_scale()
    {
        Tokens.Radius.Control.ShouldBe(8);
        Tokens.Radius.Inner.ShouldBe(10);
        Tokens.Radius.Card.ShouldBe(12);
        Tokens.Radius.Pill.ShouldBe(999);
    }

    [AvaloniaFact]
    public void No_type_is_smaller_than_ten()
    {
        double[] sizes = [Tokens.Fonts.Badge, Tokens.Fonts.Caption, Tokens.Fonts.Small, Tokens.Fonts.Body, Tokens.Fonts.Reading];
        foreach (var size in sizes) size.ShouldBeGreaterThanOrEqualTo(10);
    }

    [AvaloniaFact]
    public void Buttons_press_with_a_one_pixel_travel()
    {
        Tokens.Motion.PressTravel.ShouldBe(1);
    }
}

/// <summary>The three bundled faces must resolve at the weights the views ask for.</summary>
public sealed class FontTests
{
    [AvaloniaFact]
    public void Bundled_fonts_resolve()
    {
        var manager = Avalonia.Media.FontManager.Current;
        manager.TryGetGlyphTypeface(new Avalonia.Media.Typeface(Tokens.Fonts.Sans), out _).ShouldBeTrue("DM Sans regular");
        manager.TryGetGlyphTypeface(new Avalonia.Media.Typeface(Tokens.Fonts.Sans, weight: Avalonia.Media.FontWeight.Bold), out _).ShouldBeTrue("DM Sans bold");
        manager.TryGetGlyphTypeface(new Avalonia.Media.Typeface(Tokens.Fonts.Serif), out _).ShouldBeTrue("Very Vogue Text");
        manager.TryGetGlyphTypeface(new Avalonia.Media.Typeface(Tokens.Fonts.Display), out _).ShouldBeTrue("Perfectly Nineties");
        manager.TryGetGlyphTypeface(new Avalonia.Media.Typeface(Tokens.Fonts.SerifItalic, style: Avalonia.Media.FontStyle.Italic), out var italic).ShouldBeTrue("Very Vogue italic");
        italic!.Style.ShouldBe(Avalonia.Media.FontStyle.Italic);
        manager.TryGetGlyphTypeface(new Avalonia.Media.Typeface(Tokens.Fonts.Mono), out _).ShouldBeTrue("JetBrains Mono");
    }
}
