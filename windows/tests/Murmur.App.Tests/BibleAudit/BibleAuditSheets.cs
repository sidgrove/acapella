using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Murmur.App;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.App.Views;
using Shouldly;

namespace Murmur.AppTests.BibleAudit;

/// <summary>
/// The Bible audit of 10/10/2026 (docs/bible-audit-2026-10-10.md): options for the screens that
/// are not landing, each a real control stood in the real window and drawn onto one contact sheet
/// for Dave to choose from. Nothing here ships and nothing in the app is wired to it; when he has
/// picked, the chosen option is lifted into the app and this folder is deleted.
/// </summary>
/// <remarks>
/// The sheets are written only when ACAPELLA_VISUAL_DIR names a folder, as the visual tour's are.
/// </remarks>
public sealed class BibleAuditSheets
{
    private static readonly string? Dir = Environment.GetEnvironmentVariable("ACAPELLA_VISUAL_DIR");

    private const string Today = "Today";
    private static readonly string[] Widths = ["1080 wide, the window as it opens, on Dictations", "640 wide, the smallest window, on Dictionary"];

    [AvaloniaFact]
    public void Each_section_option_chooses_by_click_and_by_arrow_key_and_says_its_name()
    {
        foreach (var sections in new[] { SectionNavA.Build(), SectionNavB.Build(), SectionNavC.Build(), SectionNavD.Build() })
        {
            var window = new Window { Content = sections };
            window.Show();
            sections.Selected.ShouldBe(0);
            sections.Items[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            sections.Selected.ShouldBe(1);
            sections.Items[1].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
            sections.Selected.ShouldBe(0, "the arrow keys move and choose, as the web control's do");
            sections.Items.Count(i => i.IsTabStop).ShouldBe(1, "the group is one tab stop");
            foreach (var item in sections.Items)
            {
                Avalonia.Automation.AutomationProperties.GetName(item).ShouldBe(item.Word, "a mark alone still has a name");
                item.Word.ShouldNotContain("—");
                item.Word.ShouldNotContain("–");
                item.Word.ShouldNotContain("!");
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_folded_section_keeps_its_mark_and_moves_its_word_to_the_tooltip()
    {
        var sections = SectionNavA.Build();
        var window = new Window { Content = sections };
        window.Show();
        var full = Width(window, sections);
        sections.Compact = true;
        Width(window, sections).ShouldBeLessThan(full);
        ToolTip.GetTip(sections.Items[0]).ShouldBeNull("the current section keeps its word, so it needs no tip");
        ToolTip.GetTip(sections.Items[1]).ShouldBe("Dictionary");
        window.Close();

        static double Width(Window window, Control control)
        {
            window.UpdateLayout();
            return control.Bounds.Width;
        }
    }

    [AvaloniaFact]
    public void The_section_navigation_options_are_drawn_side_by_side()
    {
        if (Dir is null) return;
        Run(composition =>
        {
            var rows = new List<Sheets.Row>
            {
                new(Today, "The sections on the toggle's bed, a white thumb gliding to the current one.", Pair("nav-today", composition, null)),
                new(SectionNavA.Name, SectionNavA.Line, Pair("nav-a", composition, SectionNavA.Place)),
                new(SectionNavB.Name, SectionNavB.Line, Pair("nav-b", composition, SectionNavB.Place)),
                new(SectionNavC.Name, SectionNavC.Line, Pair("nav-c", composition, SectionNavC.Place)),
                new(SectionNavD.Name, SectionNavD.Line, Pair("nav-d", composition, SectionNavD.Place)),
            };
            Sheets.Write(Dir, "sheet-section-nav", Widths, rows, crop: 300);
        });

        static string[] Pair(string name, Composition composition, Action<Chrome, int>? place) =>
        [
            InWindow($"{name}-1080", composition, 1080, 780, dictionary: false, chrome => place?.Invoke(chrome, 0)),
            InWindow($"{name}-640", composition, 640, 480, dictionary: true, chrome => place?.Invoke(chrome, 1)),
        ];
    }

    [AvaloniaFact]
    public void The_section_navigation_options_are_drawn_in_every_state()
    {
        if (Dir is null) return;
        Directory.CreateDirectory(Dir);
        var rows = new List<Sheets.Row>
        {
            new(Today, "NavLink.Track, as shipped.", States("states-today", () =>
            {
                var links = new[] { new NavLink("Dictations", Icons.Mic, Tokens.Accent.Brand) { IsActive = true }, new NavLink("Dictionary", Icons.Book, Tokens.Accent.Emerald) { Count = 2 }, new NavLink("Settings", Icons.Sliders, Tokens.Accent.Plum) };
                return (NavLink.Track(links), i => { for (var n = 0; n < links.Length; n++) links[n].IsActive = n == i; }, i => links[i]);
            })),
            new(SectionNavA.Name, SectionNavA.Line, States("states-a", () => Group(SectionNavA.Build()))),
            new(SectionNavB.Name, SectionNavB.Line, States("states-b", () => Group(SectionNavB.Build()))),
            new(SectionNavC.Name, SectionNavC.Line, States("states-c", () =>
            {
                var (row, sections, _) = SectionNavC.BuildTogether();
                return (row, sections.Choose, i => sections.Items[i]);
            })),
            new(SectionNavD.Name, SectionNavD.Line, States("states-d", () => Group(SectionNavD.Build()))),
        };
        Sheets.Write(Dir, "sheet-section-nav-states", ["At rest, Dictations current", "Pointer over Dictionary", "Dictionary chosen", "Keyboard focus, reached by Tab"], rows, crop: 64);

        static (Control, Action<int>, Func<int, Control>) Group(SectionGroup sections) => (sections, sections.Choose, i => sections.Items[i]);
    }

    [AvaloniaFact]
    public void The_settings_options_are_drawn_side_by_side()
    {
        if (Dir is null) return;
        Run(composition =>
        {
            var rows = new List<Sheets.Row>
            {
                new(Today, "Eleven open cards, 33 settings and some 39 question marks in one scroll about 3,500 pixels long.", Pair("settings-today", composition, null)),
                new(SettingsA.Name, SettingsA.Line, Pair("settings-a", composition, SettingsA.Place)),
                new(SettingsB.Name, SettingsB.Line, Pair("settings-b", composition, SettingsB.Place)),
                new(SettingsC.Name, SettingsC.Line, Pair("settings-c", composition, SettingsC.Place)),
            };
            Sheets.Write(Dir, "sheet-settings", ["1080 wide, the window as it opens", "640 wide, the smallest window"], rows, crop: 780);
        });

        static string[] Pair(string name, Composition composition, Action<StackPanel>? place) =>
        [
            Settings($"{name}-1080", composition, 1080, 780, place),
            Settings($"{name}-640", composition, 640, 480, place),
        ];

        static string Settings(string name, Composition composition, int width, int height, Action<StackPanel>? place)
        {
            var window = new MainWindow(composition) { Width = width, Height = height };
            window.Show();
            var body = SettingsPage.Open(window);
            place?.Invoke(body);
            Sheets.Save(Dir!, window, name);
            window.Close();
            return name;
        }
    }

    [AvaloniaFact]
    public void The_status_card_options_are_drawn_side_by_side()
    {
        if (Dir is null) return;
        Run(composition =>
        {
            var rows = new List<Sheets.Row>
            {
                new(Today, "The card is pinned above all three sections; a fault is a coral bar across it with a loose Dismiss.", Pair("status-today", composition, (chrome, _, fault) => { if (fault) chrome.Window.ReportFault(StatusKit.Fault); })),
                new(StatusA.Name, StatusA.Line, Pair("status-a", composition, (chrome, _, fault) => StatusA.Place(chrome, fault))),
                new(StatusB.Name, StatusB.Line, Pair("status-b", composition, StatusB.Place)),
                new(StatusC.Name, StatusC.Line, Pair("status-c", composition, (chrome, _, fault) => StatusC.Place(chrome, fault))),
            };
            Sheets.Write(Dir, "sheet-status", ["1080 wide, on Dictations, a fault showing", "640 wide, on Dictionary, at rest"], rows, crop: 330);
        });

        static string[] Pair(string name, Composition composition, Action<Chrome, bool, bool> place) =>
        [
            InWindow($"{name}-fault-1080", composition, 1080, 780, dictionary: false, chrome => place(chrome, true, true)),
            InWindow($"{name}-rest-640", composition, 640, 480, dictionary: true, chrome => place(chrome, false, false)),
        ];
    }

    /// <summary>The real main window on the sample data, with <paramref name="place"/> standing an option in it.</summary>
    private static string InWindow(string name, Composition composition, int width, int height, bool dictionary, Action<Chrome> place)
    {
        var window = new MainWindow(composition) { Width = width, Height = height };
        window.Show();
        window.UpdateLayout();
        if (dictionary) VisualTourTests.Click(window, "Dictionary");
        window.UpdateLayout();
        place(Chrome.Of(window));
        Sheets.Save(Dir!, window, name);
        window.Close();
        return name;
    }

    /// <summary>One control alone on the canvas, in each of its four states, each a shot of the control itself.</summary>
    private static string[] States(string name, Func<(Control Root, Action<int> Choose, Func<int, Control> Item)> build)
    {
        return [Shot("rest", (_, _, _) => { }), Shot("hover", Hover), Shot("chosen", (_, choose, _) => choose(1)), Shot("focus", (_, _, item) => item(0).Focus(NavigationMethod.Tab))];

        static void Hover(Window window, Action<int> choose, Func<int, Control> item)
        {
            window.UpdateLayout();
            var target = item(1);
            var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window) ?? default;
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, at, RawInputModifiers.None);
        }

        string Shot(string state, Action<Window, Action<int>, Func<int, Control>> act)
        {
            var (root, choose, item) = build();
            root.VerticalAlignment = VerticalAlignment.Center;
            root.HorizontalAlignment = HorizontalAlignment.Left;
            var window = new Window { Width = 430, Height = 64, Background = Tokens.Canvas.Base, Content = new Border { Padding = new Thickness(16, 0), Child = root } };
            window.Show();
            act(window, choose, item);
            Sheets.Save(Dir!, window, $"{name}-{state}");
            window.Close();
            return $"{name}-{state}";
        }
    }

    private static void Run(Action<Composition> draw)
    {
        Directory.CreateDirectory(Dir!);
        var folder = Path.Combine(Path.GetTempPath(), $"acapella-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var icons = AppMark.CacheFolder;
        try
        {
            AppMark.CacheFolder = Path.Combine(folder, "icons");
            if (Environment.GetEnvironmentVariable("ACAPELLA_ICON_SEED") is { } seed && Directory.Exists(seed))
            {
                Directory.CreateDirectory(AppMark.CacheFolder);
                foreach (var file in Directory.GetFiles(seed, "*.png")) File.Copy(file, Path.Combine(AppMark.CacheFolder, Path.GetFileName(file).ToLowerInvariant()));
            }
            draw(VisualTourTests.Sample(folder));
        }
        finally
        {
            AppMark.CacheFolder = icons;
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
