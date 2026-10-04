using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Design;
using Shouldly;

namespace Murmur.AppTests;

/// <summary>The marks the Sidgrove Bible asks for: icons, tiles, chips, keycaps and app marks.</summary>
public sealed class BibleControlTests
{
    [AvaloniaFact]
    public void Every_icon_parses_and_draws()
    {
        var icons = typeof(Icons).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).ToList();
        icons.Count.ShouldBeGreaterThan(20);
        foreach (var field in icons)
        {
            var data = (string)field.GetRawConstantValue()!;
            var geometry = Should.NotThrow(() => Icons.Geometry(data), field.Name);
            geometry.Bounds.Width.ShouldBeGreaterThan(0, field.Name);
            geometry.Bounds.Right.ShouldBeLessThanOrEqualTo(24.5, $"{field.Name} must sit on the 24-unit grid");
        }

        var window = new Window { Content = new StackPanel { Children = { new Glyph(Icons.Mic), new IconTile(Icons.Book, Tokens.Accent.Emerald) } } };
        window.Show();
        window.CaptureRenderedFrame().ShouldNotBeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void A_tile_takes_its_size_and_changes_hue_with_its_state()
    {
        var tile = new IconTile(Icons.Mic, Tokens.Accent.Brand, Tokens.Layout.TileLead);
        tile.Width.ShouldBe(Tokens.Layout.TileLead);
        tile.Background.ShouldBe(Tokens.Accent.Brand.Fill);
        tile.SetAccent(Tokens.Accent.Crimson);
        tile.Background.ShouldBe(Tokens.Accent.Crimson.Fill);
        tile.BorderThickness.ShouldBe(default, "a tile is fill only, never a ring round a mark");
    }

    [AvaloniaFact]
    public void A_chip_has_a_declared_height_and_never_wraps()
    {
        var chip = new Chip("yeah → yeh", Tokens.Accent.Emerald, Icons.Book);
        chip.Height.ShouldBe(Tokens.Layout.ChipHeight);
        chip.Text.ShouldBe("yeah → yeh");
        chip.GetLogicalTextBlocks().ShouldAllBe(t => t.TextWrapping == Avalonia.Media.TextWrapping.NoWrap);
    }

    [AvaloniaFact]
    public void A_shortcut_reads_as_one_cap_a_key()
    {
        var caps = KeyCaps.Make("Win + Left Ctrl");
        caps.Children.Count.ShouldBe(2);
        caps.Children.OfType<Border>().Select(b => ((TextBlock)b.Child!).Text).ShouldBe(["Win", "Left Ctrl"]);
    }

    [AvaloniaFact]
    public void An_app_without_an_icon_wears_its_initial_in_its_own_hue_every_time()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"acapella-marks-{Guid.NewGuid():N}");
        var previous = AppMark.CacheFolder;
        AppMark.CacheFolder = folder;
        try
        {
            var mark = new AppMark("no-such-app-anywhere");
            ((TextBlock)mark.Child!).Text.ShouldBe("N");
            mark.Background.ShouldBe(AppMark.AccentFor("no-such-app-anywhere").Fill);
            AppMark.AccentFor("slack").ShouldBe(AppMark.AccentFor("SLACK"), "the hue belongs to the app, not to how its name was cased");
            Tokens.Accent.Cycle.Take(Tokens.Accent.Cycle.Count - 1).ShouldContain(AppMark.AccentFor("anything"), "slate, the grey, is the last resort and never picked");
        }
        finally { AppMark.CacheFolder = previous; }
    }

    [AvaloniaFact]
    public void An_icon_kept_from_an_earlier_read_is_used_when_the_app_is_closed()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"acapella-marks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var previous = AppMark.CacheFolder;
        AppMark.CacheFolder = folder;
        try
        {
            using (var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(4, 4), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul))
            {
                bitmap.Save(Path.Combine(folder, "closedapp.png"));
            }
            var mark = new AppMark("ClosedApp");
            mark.Child.ShouldBeOfType<Image>();
            mark.BorderBrush.ShouldBe(Tokens.Brushes.MarkEdge, "a third-party mark sits in the same light frame as every logo");
        }
        finally
        {
            AppMark.CacheFolder = previous;
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void App_names_read_the_way_a_person_says_them()
    {
        AppMark.DisplayName("OUTLOOK").ShouldBe("Outlook");
        AppMark.DisplayName("claude").ShouldBe("Claude");
        AppMark.DisplayName("WINWORD").ShouldBe("Word");
        AppMark.DisplayName("someapp").ShouldBe("Someapp");
    }

    [AvaloniaFact]
    public void A_count_rides_beside_the_section_name_and_goes_at_zero()
    {
        var link = new NavLink("Dictionary");
        var window = new Window { Content = link };
        window.Show();
        var chip = link.GetVisualDescendants().OfType<Chip>().Single();
        chip.IsVisible.ShouldBeFalse();
        link.Count = 2;
        chip.IsVisible.ShouldBeTrue();
        chip.Text.ShouldBe("2");
        link.Count = 0;
        chip.IsVisible.ShouldBeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Buttons_are_soft_boxes_not_pills()
    {
        Tokens.Radius.Button.ShouldBe(10);
        Tokens.Layout.ButtonHeight.ShouldBe(36);
        Tokens.Layout.ButtonHeightSmall.ShouldBe(32);
    }

    [AvaloniaFact]
    public void The_byline_sits_on_the_names_line_and_gives_way_when_narrow()
    {
        var mark = new Wordmark("Acapella", "by Sidgrove Intelligence");
        var window = new Window { Width = 600, Height = Tokens.Layout.CaptionHeight, Content = mark };
        window.Show();
        window.UpdateLayout();
        var texts = mark.Children.OfType<TextBlock>().ToList();
        var (name, byline) = (texts[0], texts[1]);

        mark.ShowsByline.ShouldBeTrue();
        byline.Bounds.Left.ShouldBe(name.Bounds.Right + Tokens.Layout.BylineGap, 0.5);
        (name.Bounds.Top + name.TextLayout.Baseline).ShouldBe(Tokens.Layout.CaptionBaseline, 0.5);
        (byline.Bounds.Top + byline.TextLayout.Baseline).ShouldBe(Tokens.Layout.CaptionBaseline - Tokens.Layout.BylineRaise, 0.5);
        mark.Children.OfType<LogoMark>().Single().Bounds.Center.Y.ShouldBe(Tokens.Layout.CaptionHeight / 2, 0.5);

        window.Width = 160;
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        mark.ShowsByline.ShouldBeFalse();
        byline.Bounds.Width.ShouldBe(0);
        name.Bounds.Right.ShouldBeLessThanOrEqualTo(160);
        window.Close();
    }

    [AvaloniaFact]
    public void The_title_row_is_Setlists_and_its_controls_sit_in_the_middle_of_it()
    {
        Tokens.Layout.CaptionHeight.ShouldBe(40);
        Tokens.Layout.PanelInset.ShouldBe(12);
        Tokens.Layout.LogoTile.ShouldBe(26);
        Tokens.Fonts.Wordmark.ShouldBe(22);

        var main = new Murmur.App.Views.MainWindow { Width = 1080, Height = 780 };
        try
        {
            main.Show();
            main.UpdateLayout();
            var strip = (Border)main.GetVisualDescendants().OfType<Wordmark>().Single().GetVisualParent()!.GetVisualParent()!;
            strip.Bounds.Height.ShouldBe(Tokens.Layout.CaptionHeight);
            var right = ((DockPanel)strip.Child!).Children[0];
            var centre = Tokens.Layout.CaptionHeight / 2;
            right.GetVisualDescendants().OfType<CaptionGlyph>().Count().ShouldBe(3);
            foreach (var control in right.GetVisualDescendants().OfType<CaptionGlyph>().Cast<Control>().Append(right.GetVisualChildren().OfType<Control>().First()))
            {
                var top = Avalonia.VisualExtensions.TranslatePoint(control, default(Avalonia.Point), strip)!.Value.Y;
                (top + control.Bounds.Height / 2).ShouldBe(centre, 0.5, control.GetType().Name);
            }
        }
        finally { main.Close(); }
    }
}

internal static class LogicalExtensions
{
    public static IEnumerable<TextBlock> GetLogicalTextBlocks(this Control control) =>
        control is TextBlock text ? [text] : Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control).OfType<TextBlock>();
}
