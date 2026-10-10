using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Murmur.Abstractions;
using Murmur.App;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.App.Views;
using Murmur.Core;
using Murmur.Core.Sync;
using Murmur.Dictionary;
using Shouldly;
using Xunit;

namespace Murmur.AppTests;

/// <summary>
/// The three picks from the Bible audit of 10/10/2026, as built: the house section buttons, the
/// folded Settings rows with their worked-out summaries, and the fault as a chip that is in view
/// on every section.
/// </summary>
public sealed class BibleBuildTests
{
    // ---- Section buttons ----

    [AvaloniaFact]
    public void The_sections_are_separate_buttons_with_one_tab_stop_and_arrow_keys()
    {
        var chosen = string.Empty;
        var links = new[] { new NavLink("Dictations", Icons.Mic, Tokens.Accent.MarkBrand) { IsActive = true }, new NavLink("Dictionary", Icons.Book, Tokens.Accent.MarkGreen), new NavLink("Settings", Icons.Sliders, Tokens.Accent.MarkPurple) };
        foreach (var link in links) link.Click += (_, _) => { chosen = link.Text; foreach (var other in links) other.IsActive = other == link; };
        var track = NavLink.Track(links);
        var window = new Window { Content = track };
        window.Show();

        track.Background.ShouldBeNull("no bed behind the sections: each is its own button");
        track.GetVisualDescendants().OfType<GlidePanel>().ShouldBeEmpty("and no gliding thumb; that is the toggle's");
        links.Count(l => l.IsTabStop).ShouldBe(1, "the row is one tab stop");

        links[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
        chosen.ShouldBe("Dictionary", "the arrow keys move along the row and choose");
        links[1].IsTabStop.ShouldBeTrue();
        links[1].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
        chosen.ShouldBe("Dictations");
        window.Close();
    }

    [AvaloniaFact]
    public void An_unchosen_section_is_held_on_a_fill_that_stands_off_the_canvas()
    {
        // The web's resting fill is two points off this app's canvas, where the unchosen sections
        // read as words on nothing. Ours is the house bed, and the chosen one is white on top of it.
        var rest = ((ISolidColorBrush)Tokens.Brushes.SectionRest).Color;
        var canvas = ((ISolidColorBrush)Tokens.Canvas.Base).Color;
        (canvas.R - rest.R).ShouldBeGreaterThanOrEqualTo(5);
        (canvas.G - rest.G).ShouldBeGreaterThanOrEqualTo(5);

        var link = new NavLink("Dictionary", Icons.Book, Tokens.Accent.MarkGreen);
        var window = new Window { Content = link };
        window.Show();
        var skin = link.GetVisualDescendants().OfType<Border>().First();
        skin.Background.ShouldBe(Tokens.Brushes.SectionRest);
        skin.BorderThickness.ShouldBe(default, "fill only, no outline");
        var before = Width(window, link);
        link.IsActive = true;
        skin.Background.ShouldBe(Tokens.Brushes.Card);
        Width(window, link).ShouldBe(before, 0.01, "going bold does not widen the button");
        link.GetVisualDescendants().OfType<IconTile>().Single().Width.ShouldBe(Tokens.Layout.TileSmall, "the hue lives on a 22px tile");
        window.Close();

        static double Width(Window window, Control control)
        {
            window.UpdateLayout();
            return control.Bounds.Width;
        }
    }

    [AvaloniaFact]
    public void Key_focus_is_the_brand_halo_on_every_button()
    {
        var window = new MainWindow();
        window.Show();
        window.UpdateLayout();
        foreach (var button in window.GetVisualDescendants().OfType<Button>())
        {
            var adorner = button.FocusAdorner.ShouldNotBeNull($"{button.GetType().Name} has a focus style").Build().ShouldBeOfType<Border>();
            adorner.BorderBrush.ShouldBe(Tokens.Brushes.FocusHalo, $"{button.GetType().Name} wears the halo, not the theme's black box");
            adorner.BorderThickness.Left.ShouldBe(Tokens.Border.FocusHalo);
        }
        window.Close();
    }

    // ---- The fault ----

    [AvaloniaFact]
    public void A_fault_is_a_chip_on_the_status_line_and_follows_the_user_to_the_other_sections()
    {
        const string message = "The microphone could not be opened.";
        var window = new MainWindow { Width = 1080, Height = 780 };
        window.Show();
        window.ReportFault(message);
        window.UpdateLayout();

        var onCard = window.FaultNotice;
        onCard.IsEffectivelyVisible.ShouldBeTrue();
        onCard.Bounds.Height.ShouldBe(Tokens.Layout.ChipHeight, "a chip, not a bar");
        onCard.Bounds.Width.ShouldBeLessThanOrEqualTo(Tokens.Layout.FaultChipWidth);
        ToolTip.GetTip(onCard).ShouldBe(message, "the whole sentence is one hover away");
        var toggle = window.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().Single();
        Math.Abs(Middle(onCard) - Middle(toggle)).ShouldBeLessThan(8, "it sits on the status line itself");

        foreach (var open in new Action[] { window.ShowSettings, () => Section(window, "Dictionary") })
        {
            open();
            window.UpdateLayout();
            onCard.IsEffectivelyVisible.ShouldBeFalse("the card has left the page");
            var onTitle = window.FaultNotice;
            onTitle.ShouldNotBeSameAs(onCard);
            onTitle.IsEffectivelyVisible.ShouldBeTrue("but the fault has not: it stands beside the title");
            onTitle.Bounds.Width.ShouldBeGreaterThan(0);
            onTitle.Message.ShouldBe(message);
        }

        window.FaultNotice.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        window.FaultNotice.IsVisible.ShouldBeFalse("pressing the chip clears it");
        Section(window, "Dictations");
        window.FaultNotice.IsVisible.ShouldBeFalse("on every section");
        window.Close();

        double Middle(Control control) => control.TranslatePoint(new Avalonia.Point(0, control.Bounds.Height / 2), window)!.Value.Y;
    }

    [AvaloniaFact]
    public void A_fault_beside_the_title_keeps_its_mark_when_the_window_is_narrow()
    {
        var folder = Folder();
        var window = new MainWindow(Preview(folder)) { Width = 640, Height = 480 };
        try
        {
            window.Show();
            Section(window, "Dictionary");
            window.ReportFault("AI clean-up did not respond, so the local transcript was typed.");
            for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            var chip = window.FaultNotice;
            chip.IsEffectivelyVisible.ShouldBeTrue();
            chip.Bounds.Width.ShouldBeGreaterThanOrEqualTo(Tokens.Layout.ChipHeight, "at least its warning mark is on screen");
            var page = window.GetVisualDescendants().OfType<WashPanel>().Single();
            var right = chip.TranslatePoint(default, window)!.Value.X + chip.Bounds.Width;
            right.ShouldBeLessThanOrEqualTo(page.TranslatePoint(default, window)!.Value.X + page.Bounds.Width - 16, "and it is not cut off");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    // ---- Settings: the folded rows ----

    [AvaloniaFact]
    public void Settings_is_eleven_folded_rows_and_every_setting_is_still_inside_them()
    {
        var folder = Folder();
        var composition = Preview(folder);
        var window = new MainWindow(composition) { Width = 1080, Height = 780 };
        try
        {
            window.Show();
            window.ShowSettings();
            window.UpdateLayout();
            var view = window.GetVisualDescendants().OfType<SettingsView>().Single();
            view.Rows.Select(r => r.Title).ShouldBe(["Push to talk", "Microphone", "Sounds", "Hearing you", "Writing", "Sending", "AI clean-up", "Learning from you", "Jev decisions", "Sync", "Behaviour"]);
            view.Rows.ShouldAllBe(r => !r.IsOpen && r.Summary.Length > 0);

            // Folded, no setting is on screen; the page fits the window without scrolling.
            view.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().ShouldAllBe(s => !s.IsEffectivelyVisible);
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Extent.Height.ShouldBeLessThanOrEqualTo(scroll.Viewport.Height + 1, "eleven lines fit one screen at the window's own size");

            // Nothing was taken away: the same switches, fields and toggles are there to be opened.
            view.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().Count().ShouldBe(17);
            view.GetVisualDescendants().OfType<TextBox>().Count().ShouldBeGreaterThanOrEqualTo(11);
            view.GetVisualDescendants().OfType<Segmented>().Count().ShouldBeGreaterThanOrEqualTo(2);
            view.GetVisualDescendants().OfType<SyncPart>().ShouldHaveSingleItem();
            view.GetVisualDescendants().OfType<ModelPart>().ShouldHaveSingleItem();

            var sounds = view.Rows[2];
            sounds.Press();
            window.UpdateLayout();
            sounds.IsOpen.ShouldBeTrue();
            var switches = sounds.GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().ToList();
            switches.Count.ShouldBe(2);
            switches.ShouldAllBe(s => s.IsEffectivelyVisible);
            view.Rows[4].GetVisualDescendants().OfType<Murmur.App.Controls.Switch>().ShouldAllBe(s => !s.IsEffectivelyVisible, "one row opening leaves the others as they were");

            // A switch flipped inside an open row saves as it always did, and the row's line follows.
            sounds.Summary.ShouldBe("Recording and send sounds on");
            switches[1].IsChecked = false;
            composition.Settings.Data.SendSound.ShouldBeFalse();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            sounds.Summary.ShouldBe("Recording sounds only");
            sounds.Press();
            sounds.IsOpen.ShouldBeFalse();
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void A_row_with_a_problem_says_so_in_coral_never_red()
    {
        var row = new DisclosureRow(Icons.Mic, Tokens.Accent.Info, "Microphone", null, new Border());
        row.Say("Windows default");
        row.HasProblem.ShouldBeFalse();
        row.Say("No microphone found", problem: true);
        row.HasProblem.ShouldBeTrue();
        var window = new Window { Content = row };
        window.Show();
        var words = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "No microphone found");
        words.Foreground.ShouldBe(Tokens.Accent.Coral.Ink);
        words.Foreground.ShouldNotBe(Tokens.Accent.Crimson.Ink, "red means recording and nothing else");
        window.Close();
    }

    // ---- Settings: each section's line is worked out from what is stored ----

    private static SettingsData Data => new();

    [Fact]
    public void Push_to_talk_names_the_key_and_how_it_is_used()
    {
        SettingsSummary.PushToTalk(Data).Text.ShouldBe("Right Ctrl, hold or tap");
        SettingsSummary.PushToTalk(Data with { Mode = ActivationMode.Hold }).Text.ShouldBe("Right Ctrl, hold to talk");
        SettingsSummary.PushToTalk(Data with { Mode = ActivationMode.Tap }).Text.ShouldBe("Right Ctrl, tap to start and stop");
        SettingsSummary.PushToTalk(Data with { PushToTalkKey = 0x14 }).Text.ShouldStartWith(KeyNames.Describe(0x14, 0));
    }

    [Fact]
    public void Microphone_names_the_one_chosen_and_says_when_there_is_none()
    {
        AudioDevice[] devices = [new("a", "Headset", true), new("b", "Desk mic", false)];
        SettingsSummary.Microphone(Data, devices).ShouldBe(new("Windows default"));
        SettingsSummary.Microphone(Data with { MicrophoneDeviceId = "b" }, devices).ShouldBe(new("Desk mic"));
        SettingsSummary.Microphone(Data, []).ShouldBe(new("No microphone found", Problem: true));
        SettingsSummary.Microphone(Data with { MicrophoneDeviceId = "gone" }, devices).Problem.ShouldBeTrue("a chosen microphone that is unplugged is a problem to say");
        SettingsSummary.Microphone(Data, null).Problem.ShouldBeFalse();
    }

    [Fact]
    public void Sounds_says_which_cues_are_on()
    {
        SettingsSummary.Sounds(Data).Text.ShouldBe("Recording and send sounds on");
        SettingsSummary.Sounds(Data with { SendSound = false }).Text.ShouldBe("Recording sounds only");
        SettingsSummary.Sounds(Data with { RecordingSounds = false }).Text.ShouldBe("Send sound only");
        SettingsSummary.Sounds(Data with { RecordingSounds = false, SendSound = false }).Text.ShouldBe("Off");
    }

    [Fact]
    public void Hearing_says_where_and_owns_up_to_a_missing_model_or_key()
    {
        SettingsSummary.Hearing(Data, modelInstalled: true, modelReady: true, downloading: false, cloudKey: false).ShouldBe(new("On this PC"));
        SettingsSummary.Hearing(Data, true, false, false, false).Text.ShouldBe("On this PC, loading");
        SettingsSummary.Hearing(Data with { CloudTranscription = true }, true, true, false, cloudKey: true).ShouldBe(new("On this PC and in the cloud"));
        SettingsSummary.Hearing(Data with { CloudTranscription = true }, true, true, false, cloudKey: false).Problem.ShouldBeTrue("the cloud switched on with no key is a problem");
        SettingsSummary.Hearing(Data, modelInstalled: false, false, false, false).ShouldBe(new("The speech model is not downloaded yet", Problem: true));
        SettingsSummary.Hearing(Data, false, false, downloading: true, false).ShouldBe(new("Downloading the speech model"));
    }

    [Fact]
    public void Writing_follows_the_full_stop_rule_and_counts_the_rules_that_are_on()
    {
        var all = Data with { NoCommaBeforeAnd = true, SpokenCommands = true, RemoveFillers = true, BritishSpelling = true, JoinDictations = true, FullStops = TrailingFullStop.DropAfterSingleSentence };
        SettingsSummary.Writing(all).Text.ShouldBe("Full stop dropped after one sentence, 5 of 5 rules on");
        SettingsSummary.Writing(all with { FullStops = TrailingFullStop.Never }).Text.ShouldStartWith("Never ends with a full stop");
        SettingsSummary.Writing(all with { FullStops = TrailingFullStop.Keep }).Text.ShouldStartWith("Full stops kept");
        foreach (var one in new[] { all with { SpokenCommands = false }, all with { RemoveFillers = false }, all with { NoCommaBeforeAnd = false }, all with { BritishSpelling = false }, all with { JoinDictations = false } })
            SettingsSummary.Writing(one).Text.ShouldEndWith("4 of 5 rules on");
    }

    [Fact]
    public void Sending_says_what_to_say()
    {
        SettingsSummary.Sending(Data).Text.ShouldBe("Say “blob” or “send it”");
        SettingsSummary.Sending(Data with { SendWord = "send" }).Text.ShouldBe("Say “send” or “send it”");
        SettingsSummary.Sending(Data with { SendOnlyPhrase = " " }).Text.ShouldBe("Say “blob”");
        SettingsSummary.Sending(Data with { SendWord = string.Empty }).Text.ShouldBe("Say “send it”");
        SettingsSummary.Sending(Data with { SendWord = string.Empty, SendOnlyPhrase = string.Empty }).Text.ShouldBe("Off");
    }

    [Fact]
    public void Clean_up_names_the_mode_and_model_and_says_when_its_key_is_missing()
    {
        SettingsSummary.CleanUp(Data with { AiCleanup = false }, true, true).ShouldBe(new("Instant, typed as heard"));
        var polished = Data with { AiCleanup = true };
        SettingsSummary.CleanUp(polished, geminiKey: true, anthropicKey: false).ShouldBe(new($"Polished by {Murmur.Speech.GeminiCleaner.DefaultModel}"));
        SettingsSummary.CleanUp(polished with { CustomInstructions = "Use English spellings" }, true, false).Text.ShouldEndWith("with your own instructions");
        var noGemini = SettingsSummary.CleanUp(polished, geminiKey: false, anthropicKey: true);
        noGemini.Problem.ShouldBeTrue();
        noGemini.Text.ShouldContain("no Gemini key");

        // The Anthropic key row main added: a Claude model needs that key, not Gemini's.
        var claude = polished with { GeminiModel = Murmur.Speech.ClaudeCleaner.DefaultModel };
        SettingsSummary.CleanUp(claude, geminiKey: false, anthropicKey: true).ShouldBe(new($"Polished by {Murmur.Speech.ClaudeCleaner.DefaultModel}"));
        var noAnthropic = SettingsSummary.CleanUp(claude, geminiKey: true, anthropicKey: false);
        noAnthropic.Problem.ShouldBeTrue();
        noAnthropic.Text.ShouldContain("no Anthropic key");
    }

    [Fact]
    public void Learning_says_whether_it_learns_and_what_it_does_with_a_fix()
    {
        var learning = Data with { LearnFromEdits = true, AddLearntFixes = true, KeepRecordings = false };
        SettingsSummary.Learning(learning).Text.ShouldBe("On, adds sure fixes by itself");
        SettingsSummary.Learning(learning with { AddLearntFixes = false }).Text.ShouldBe("On, asks before adding a word");
        SettingsSummary.Learning(learning with { KeepRecordings = true }).Text.ShouldEndWith("keeps corrected recordings");
        SettingsSummary.Learning(learning with { LearnFromEdits = false }).Text.ShouldBe("Off");
    }

    [Fact]
    public void Jev_is_on_with_a_key_and_says_it_is_off_with_none_from_anywhere()
    {
        SettingsSummary.Jev(key: true).ShouldBe(new("On"));
        SettingsSummary.Jev(key: false).ShouldBe(new("Off, with no key from anywhere"));
    }

    [Fact]
    public void Sync_says_who_and_when_and_puts_a_problem_first()
    {
        var now = new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero);
        SettingsSummary.Sync(null, now).ShouldBe(new("Not signed in"));
        SettingsSummary.Sync(new SyncStatus(true, null, null, false, null), now).ShouldBe(new("Not signed in"));
        SettingsSummary.Sync(new SyncStatus(true, "dave@sidgrove.com", now.AddMinutes(-4), false, null), now).ShouldBe(new("dave@sidgrove.com, synced 4 minutes ago"));
        SettingsSummary.Sync(new SyncStatus(true, "dave@sidgrove.com", null, true, null), now).Text.ShouldEndWith("syncing now");
        SettingsSummary.Sync(new SyncStatus(true, "dave@sidgrove.com", now, false, "The last sync could not reach Sidgrove Intelligence."), now).ShouldBe(new("The last sync could not reach Sidgrove Intelligence.", Problem: true));
    }

    [Fact]
    public void Behaviour_says_where_the_words_go_and_what_is_kept()
    {
        var data = Data with { InjectText = true, KeepHistory = true, DuckOtherAudio = false };
        SettingsSummary.Behaviour(data, startsWithWindows: null).Text.ShouldBe("Types into your app, keeps a history");
        SettingsSummary.Behaviour(data with { InjectText = false }, null).Text.ShouldStartWith("Copies to the clipboard only");
        SettingsSummary.Behaviour(data with { KeepHistory = false }, null).Text.ShouldContain("no history");
        SettingsSummary.Behaviour(data with { DuckOtherAudio = true }, null).Text.ShouldEndWith("mutes other audio");
        SettingsSummary.Behaviour(data, startsWithWindows: true).Text.ShouldEndWith("starts with Windows");
    }

    [Fact]
    public void No_summary_carries_a_dash_or_an_exclamation_mark()
    {
        var now = DateTimeOffset.Now;
        string[] lines =
        [
            SettingsSummary.PushToTalk(Data).Text, SettingsSummary.Microphone(Data, []).Text, SettingsSummary.Microphone(Data with { MicrophoneDeviceId = "x" }, [new("a", "A", true)]).Text,
            SettingsSummary.Sounds(Data).Text, SettingsSummary.Hearing(Data with { CloudTranscription = true }, true, true, false, false).Text, SettingsSummary.Hearing(Data, false, false, false, false).Text,
            SettingsSummary.Writing(Data).Text, SettingsSummary.Sending(Data).Text, SettingsSummary.CleanUp(Data with { AiCleanup = true }, false, false).Text, SettingsSummary.CleanUp(Data, true, true).Text,
            SettingsSummary.Learning(Data).Text, SettingsSummary.Jev(false).Text, SettingsSummary.Sync(null, now).Text, SettingsSummary.Behaviour(Data, true).Text,
        ];
        foreach (var line in lines)
        {
            line.ShouldNotContain("—");
            line.ShouldNotContain("–");
            line.ShouldNotContain("!");
        }
    }

    private static void Section(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<NavLink>().Single(l => l.Text == name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"acapella-bible-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Composition Preview(string folder)
    {
        var composition = Composition.ForPreview(
            new AppSettings(Path.Combine(folder, "s.json")), new DictionaryFile(Path.Combine(folder, "d.txt")),
            new TranscriptStore(Path.Combine(folder, "t.jsonl")), new SuggestionStore(Path.Combine(folder, "g.json")));
        composition.Settings.Update(composition.Settings.Data with { HasOnboarded = true });
        composition.Transcripts.Add(new TranscriptRecord { Text = "One dictation, so the history has its tools.", RawText = "One dictation" });
        return composition;
    }
}
