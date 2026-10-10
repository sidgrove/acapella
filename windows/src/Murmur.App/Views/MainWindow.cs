using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.Abstractions;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// The main window: a serif title naming the view with the section switcher beside it, one
/// status card that says what Acapella is doing and how to start it, then the history, the
/// dictionary or the settings.
/// </summary>
/// <remarks>
/// Built to the Sidgrove Bible (Sidgrove Intelligence <c>docs/BIBLE.md</c>, 29/09/2026): the
/// answer first in one calm card, colour carried by small tinted tiles, nothing repeated and
/// nothing explained that the design should make obvious.
/// </remarks>
public sealed class MainWindow : ShellWindow
{
    private readonly Composition? _composition;
    private readonly TextBlock _title;
    private readonly IconTile _stateTile;
    private readonly TextBlock _stateTitle;
    private readonly TextBlock _counter;
    private readonly Border _shortcut;
    private readonly TextBlock _readoutLabel;
    private readonly LevelBars _bars;
    private readonly WorkingDots _working = new() { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly NavLink _transcriptionsLink;
    private readonly NavLink _dictionaryLink;
    private readonly NavLink _settingsLink;
    private SettingsView? _settingsView;
    private readonly ContentControl _sectionHost;
    private readonly Border _toolsHost = new() { VerticalAlignment = VerticalAlignment.Center };
    // One fault, two places it can stand: on the status card's own line while Dictations is the
    // page, and beside the title on the other two sections, where the card is not shown.
    private readonly FaultChip _faultOnCard = new() { Margin = new Thickness(Tokens.Space.Snug, 0, 0, 0) };
    private readonly FaultChip _faultOnTitle = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private string? _faultMessage;
    private Border? _status;
    private bool _onDictations = true;
    private readonly DispatcherTimer _poll;
    private readonly OverlayWindow? _overlay;
    private Controls.Switch? _enabled;

    private TranscriptionsView? _transcriptionsView;
    private DictionaryView? _dictionaryView;
    private readonly TextBlock _preview;
    private DateTimeOffset? _startedAt;
    private string _lastState = string.Empty;
    private string _lastPreview = string.Empty;
    private string _lastHint = string.Empty;

    /// <summary>Builds a window with no engine behind it. Used by headless tests.</summary>
    public MainWindow() : this(null) { }

    /// <summary>Builds the window over <paramref name="composition"/>.</summary>
    public MainWindow(Composition? composition)
    {
        _composition = composition;

        Title = AppPaths.ProductName;
        MinWidth = Tokens.Layout.MainMinWidth;
        MinHeight = Tokens.Layout.MainMinHeight;
        Width = Tokens.Layout.MainWidth;
        Height = Tokens.Layout.MainHeight;

        _title = new TextBlock
        {
            FontFamily = Tokens.Fonts.Serif,
            FontSize = Tokens.Fonts.Title,
            LineHeight = Tokens.Fonts.Title * 1.25,
            LetterSpacing = Tokens.Fonts.TitleTracking,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _stateTile = new IconTile(Icons.Mic, Tokens.Accent.Brand, Tokens.Layout.TileLead);
        _stateTitle = Text.Heading("Ready");
        _stateTitle.TextWrapping = TextWrapping.NoWrap;
        _stateTitle.VerticalAlignment = VerticalAlignment.Center;
        _counter = Text.Number("00:00", Tokens.Fonts.Heading, Tokens.Brushes.Muted);
        _counter.VerticalAlignment = VerticalAlignment.Center;
        _counter.IsVisible = false;
        _shortcut = new Border { VerticalAlignment = VerticalAlignment.Center };
        _readoutLabel = Text.Eyebrow("IDLE");
        _bars = new LevelBars(Tokens.Layout.BarsCountSmall, Tokens.Layout.BarsHeightSmall) { VerticalAlignment = VerticalAlignment.Center };
        _preview = Text.Muted(string.Empty);
        _preview.TextWrapping = TextWrapping.Wrap;
        _preview.IsVisible = false;

        _transcriptionsLink = new NavLink("Dictations", Icons.Mic, Tokens.Accent.MarkBrand) { IsActive = true };
        _dictionaryLink = new NavLink("Dictionary", Icons.Book, Tokens.Accent.MarkGreen);
        _settingsLink = new NavLink("Settings", Icons.Sliders, Tokens.Accent.MarkPurple);
        _transcriptionsLink.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryLink.Click += (_, _) => ShowSection(transcriptions: false);
        _settingsLink.Click += (_, _) => ShowSettings();
        ToolTip.SetTip(_dictionaryLink, "Fixes you made by hand that are waiting for a yes or a no show here as a count.");
        if (_composition is { } suggested)
        {
            // The count is how the user finds out there is something to look at; nothing
            // pops up over their work to say so.
            void Count() => _dictionaryLink.Count = DictionaryView.PendingSuggestions(suggested.Suggestions, suggested.Dictionary);
            suggested.Suggestions.Changed += (_, _) => Dispatcher.UIThread.Post(Count);
            suggested.Dictionary.Changed += (_, _) => Dispatcher.UIThread.Post(Count);
            Count();
        }

        _faultOnCard.Click += (_, _) => DismissFault();
        _faultOnTitle.Click += (_, _) => DismissFault();

        _sectionHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tokens.Motion.PanelPoll };
        _poll.Tick += (_, _) => SyncFromEngine();
        _poll.Start();

        if (_composition is not null) _overlay = new OverlayWindow(PlatformFactory.CreateWindowTweaks());

        // The mark alone top left, no name and no byline: the window's OS title still says Acapella
        // for the taskbar and screen readers (Dave, 05/10/2026: "just keep it sharp").
        Content = Frame(null, BuildBody());
        BindShortcuts();
        ShowSection(transcriptions: true);
        RefreshHint();

        if (_composition?.Engine is { } engine)
        {
            var audio = PlatformFactory.CreateFeedbackAudio();
            var feedback = new FeedbackSounds(() => _composition.Settings.Data, wave => audio?.Play(wave));
            // The cue is decided on the engine's thread, the moment the state changes, and
            // needs no dispatcher: the player is off-thread itself. The panel refresh is
            // coalesced, because Changed fires for every audio chunk (a hundred a second,
            // sixty of them in one burst as the pre-roll lands) and each post used to
            // re-present the overlay.
            var refreshPending = 0;
            engine.Changed += (_, _) =>
            {
                var state = engine.State == DictationState.Recording && !engine.IsCaptureReady
                    ? DictationState.Idle : engine.State;
                feedback.Observe(state);
                if (Interlocked.Exchange(ref refreshPending, 1) == 0)
                {
                    Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref refreshPending, 0); SyncFromEngine(); });
                }
            };
            engine.Faulted += (_, message) => Dispatcher.UIThread.Post(() =>
            {
                ShowFault(message);
                // A tick must never stand over a fault: the words that could not be typed are the truth.
                if (_overlay?.IsShowingDone == true) _overlay.Dismiss();
            });
            engine.Completed += (_, result) =>
            {
                if (result.CleanupFailed && !engine.IsFaultedRecently)
                {
                    Dispatcher.UIThread.Post(() => ShowFault("The clean-up rewrote rather than tidied, so your words were typed as heard. Both are in the history."));
                }
                // No "Done" once the words land (Dave, 10/10/2026: "it's in the way of my text
                // interface ... Just push it into the box"): the words in the box are the
                // confirmation. Only the exception speaks, that they went in as heard.
                if (result.CleanupFailed)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (engine.State == DictationState.Recording) return;
                        _overlay?.ShowDone(asHeard: true);
                    });
                }
            };
            engine.CopyTranscriptAsync = async text =>
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (Clipboard is { } clipboard)
                        await clipboard.SetTextAsync(text).ConfigureAwait(true);
                });
            };
            engine.Sent += (_, _) => Dispatcher.UIThread.Post(() => { feedback.Sent(); _overlay?.ShowSendFeedback(); });
            engine.Dropped += (_, reason) => Dispatcher.UIThread.Post(() => _overlay?.ShowNotice(reason));
            engine.Start();
            _ = PreloadAsync(engine);
        }

        Opened += (_, _) =>
        {
            if (_composition is not null && !_composition.Settings.Data.HasOnboarded) ShowWelcome();
        };
    }

    /// <summary>Opens the first-run walkthrough.</summary>
    public void ShowWelcome()
    {
        if (_composition is null) return;
        var welcome = new WelcomeWindow(_composition);
        welcome.ModelChanged += (_, _) => ModelChanged();
        welcome.Closed += (_, _) => RefreshHint();
        _ = welcome.ShowDialog(this);
    }

    private async Task PreloadAsync(DictationEngine engine)
    {
        var ready = await engine.PreloadAsync(CancellationToken.None).ConfigureAwait(true);
        if (!ready) ShowFault(DictationEngine.ModelMissingMessage);
        RefreshHint();
        // The Settings status was computed before the model finished loading and would
        // otherwise say "found, loading" for the rest of the session.
        _settingsView?.RefreshModel();
    }

    /// <summary>
    /// Types the most recent transcript again, wherever the caret is now. Also reachable
    /// from the tray, for text that landed in the wrong window.
    /// </summary>
    public void RetypeLast()
    {
        var records = _composition?.Transcripts.Records;
        if (_composition?.Engine is not { } engine || records is not { Count: > 0 }) return;
        _ = engine.RetypeAsync(records[0].Text);
    }

    /// <summary>Re-checks the model after a download from Settings.</summary>
    public void ModelChanged()
    {
        if (_composition?.Engine is { } engine) _ = PreloadAsync(engine);
        RefreshHint();
    }

    /// <summary>
    /// Which mode is in use, as one small chip on the status line: a bolt for Instant, sparkles
    /// for Polished. The choice itself lives in Settings (Dave, 05/10/2026: "maybe that should
    /// just be settings as well"), so the title row is only the mark; the chip is the door there.
    /// </summary>
    private Border BuildModeChip()
    {
        var host = new Border { VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Margin = new Thickness(Tokens.Space.Snug, 0, 0, 0) };
        void Refresh()
        {
            var polished = _composition?.Settings.Data.AiCleanup == true;
            host.Child = polished
                ? new Chip("Polished", Tokens.Accent.Plum, Icons.Sparkles)
                : new Chip("Instant", Tokens.Accent.Amber, Icons.Zap);
            ToolTip.SetTip(host, polished
                ? "Polished: tidied by Gemini before it's typed. Change it in Settings."
                : "Instant: typed exactly as heard, on this machine. Change it in Settings.");
        }
        host.PointerPressed += (_, e) => { if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) ShowSettings(); };
        if (_composition is not null)
            _composition.Settings.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();
        return host;
    }

    private Border BuildBody()
    {
        var body = new DockPanel { ClipToBounds = false };
        body.Children.Add(Panels.Docked(BuildMasthead(), Dock.Top));
        _status = BuildStatus();
        body.Children.Add(Panels.Docked(_status, Dock.Top));
        body.Children.Add(_sectionHost);
        return new Border { Child = body, MaxWidth = Tokens.Layout.MainContentMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Roomy) };
    }

    /// <summary>
    /// One row: the title on the left, then the section's own tools (search, its one action and
    /// the "..." menu) and the sections on the right. The page has no toolbar strip of its own.
    /// </summary>
    private Grid BuildMasthead()
    {
        var tabs = NavLink.Track(_transcriptionsLink, _dictionaryLink, _settingsLink);
        // The sections sit beside the title (its titleAside), the page's tools at the far right.
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, Tokens.Space.Wide, Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy),
        };
        tabs.Margin = new Thickness(Tokens.Space.Wide, Tokens.Space.Tight, 0, 0);
        _toolsHost.Margin = new Thickness(Tokens.Space.Base, Tokens.Space.Tight, 0, 0);
        // A fault raised while the status card is off the page (Dictionary, Settings) stands here,
        // in the air between the sections and the tools, so it is never out of sight.
        _faultOnTitle.Margin = new Thickness(Tokens.Space.Base, Tokens.Space.Tight, 0, 0);
        Grid.SetColumn(tabs, 1);
        Grid.SetColumn(_faultOnTitle, 2);
        Grid.SetColumn(_toolsHost, 3);
        row.Children.Add(_title);
        row.Children.Add(_toolsHost);
        row.Children.Add(tabs);
        row.Children.Add(_faultOnTitle);
        // A narrow window takes the title down a size rather than cutting it short.
        row.SizeChanged += (_, e) =>
        {
            _title.FontSize = e.NewSize.Width < Tokens.Layout.NarrowTitleBelow ? Tokens.Fonts.TitleNarrow : Tokens.Fonts.Title;
            // Narrow, the page's one action keeps its icon and gives up its words.
            _dictionaryView?.SetCompact(e.NewSize.Width < Tokens.Layout.CompactToolsBelow);
        };
        // Nothing clips: when the title, every section's name and the page's tools cannot share the
        // row, the sections that are not current fold to their marks. Asked after each layout from
        // what each piece wants, with the sections' full width remembered, so it settles in one pass.
        var links = new[] { _transcriptionsLink, _dictionaryLink, _settingsLink };
        var tabsFull = 0d;
        row.LayoutUpdated += (_, _) =>
        {
            if (row.Bounds.Width <= 0) return;
            if (!links[0].IsCompact) tabsFull = tabs.DesiredSize.Width;
            // A fault beside the title needs at least its mark's room, and gives up its words
            // before anything on the row is cut.
            var fault = _faultOnTitle.IsVisible ? Tokens.Layout.ChipHeight + Tokens.Space.Base : 0;
            var wanted = _title.DesiredSize.Width + tabsFull + _toolsHost.DesiredSize.Width + fault;
            var compact = wanted > row.Bounds.Width;
            var room = row.Bounds.Width - _title.DesiredSize.Width - tabs.DesiredSize.Width - _toolsHost.DesiredSize.Width;
            _faultOnTitle.IsBare = room < Tokens.Layout.FaultChipMinWidth;
            if (compact == links[0].IsCompact) return;
            foreach (var link in links) link.IsCompact = compact;
        };
        return row;
    }

    private void SetTitle(string plain, string accent)
    {
        _title.Inlines =
        [
            new Run(plain),
            new Run(accent) { FontStyle = FontStyle.Italic, Foreground = Tokens.Brushes.BrandStrong },
        ];
    }

    /// <summary>
    /// The status card, one line: a tile whose hue is the state, the state in words, the
    /// shortcut as keycaps, the voice bars and the switch. The bars are always there, breathing
    /// faintly at rest and rising with the voice, so the card is alive before anyone speaks; a
    /// fault is one small coral chip on the line itself, never a bar under it.
    /// </summary>
    private Border BuildStatus()
    {
        _enabled = new Controls.Switch { IsChecked = _composition?.Settings.Data.IsEnabled ?? true, VerticalAlignment = VerticalAlignment.Center };
        _enabled.IsCheckedChanged += (_, _) => SetEnabled(_enabled.IsChecked == true);
        ToolTip.SetTip(_enabled, "Listening for your shortcut. Switch off to pause Acapella without quitting it.");
        Avalonia.Automation.AutomationProperties.SetName(_enabled, "Listen for the shortcut");

        var stateLine = Panels.Row(Tokens.Space.Snug, _stateTitle, _counter);
        _shortcut.Margin = new Thickness(Tokens.Space.Snug, 0, 0, 0);
        var words = Panels.Row(Tokens.Space.Snug, stateLine, _shortcut, BuildModeChip(), _faultOnCard);
        words.MinHeight = Tokens.Layout.TileLead;

        // The bars' place holds one thing at a time: the bars, or the loader's dots while the words
        // are being worked on (bars with nothing to follow read as stalled). It keeps the bars' width.
        _working.HorizontalAlignment = HorizontalAlignment.Right;
        var meter = new Panel { Width = _bars.Width, VerticalAlignment = VerticalAlignment.Center, Children = { _bars, _working } };
        var right = Panels.Row(Tokens.Space.Roomy, meter, _enabled);
        var top = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(Panels.Row(Tokens.Space.Base, _stateTile, words));

        _preview.Margin = new Thickness(Tokens.Layout.TileLead + Tokens.Space.Base, Tokens.Space.Snug, 0, 0);
        var card = Card.Standard(Panels.Column(0, top, _preview), Tokens.Space.Roomy);
        card.Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Base, Tokens.Space.Wide, Tokens.Space.Base);
        // Narrow, the resting waveform gives way so the state, the key and the mode never crowd,
        // and a fault keeps its mark and gives up its words (they stay in its tooltip).
        card.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < Tokens.Layout.StatusBarsBelow;
            meter.Opacity = narrow ? 0 : 1;
            _faultOnCard.IsBare = narrow;
        };
        card.Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, 0, Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy);
        return card;
    }

    /// <summary>
    /// Which section is on the page. The status card is Dictations' own answer and stands only
    /// there (it was the same card above all three sections; audit of 10/10/2026); a fault moves
    /// to the title row on the other two, so it is in view wherever the user is.
    /// </summary>
    private void SetPage(bool dictations)
    {
        _onDictations = dictations;
        if (_status is not null) _status.IsVisible = dictations;
        PlaceFault();
    }

    private void PlaceFault()
    {
        var showing = _faultMessage is not null;
        _faultOnCard.IsVisible = showing && _onDictations;
        _faultOnTitle.IsVisible = showing && !_onDictations;
    }

    private void BindShortcuts()
    {
        void Bind(Key key, KeyModifiers modifiers, Action action) =>
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = new Relay(action) });

        Bind(Key.R, KeyModifiers.Control, ToggleRecording);
        Bind(Key.OemComma, KeyModifiers.Control, ShowSettings);
        Bind(Key.F, KeyModifiers.Control, FocusSearch);
        Bind(Key.N, KeyModifiers.Control, () => { ShowSection(false); _dictionaryView?.AddEntry(); });
        Bind(Key.D1, KeyModifiers.Control, () => ShowSection(true));
        Bind(Key.D2, KeyModifiers.Control, () => ShowSection(false));
        Bind(Key.C, KeyModifiers.Control | KeyModifiers.Shift, CopyLast);
        Bind(Key.W, KeyModifiers.Control, () => { _settingsView?.Flush(); Hide(); });
        Bind(Key.Q, KeyModifiers.Control, () => { _settingsView?.Flush(); App.Quit(); });
        Bind(Key.F1, KeyModifiers.None, ShowAbout);
        Bind(Key.L, KeyModifiers.Control | KeyModifiers.Shift, () => OpenPath(Log.Path));
    }

    private void ShowSection(bool transcriptions)
    {
        _settingsLink.IsActive = false;
        SetPage(transcriptions);
        RefreshHint();
        _transcriptionsLink.IsActive = transcriptions;
        _dictionaryLink.IsActive = !transcriptions;
        if (transcriptions) SetTitle("Your ", "dictations"); else SetTitle("Your ", "dictionary");

        if (_composition is null)
        {
            _sectionHost.Content = transcriptions
                ? Panels.EmptyState(Icons.Mic, Tokens.Accent.Brand, "No dictations yet. Press your shortcut and speak.")
                : Panels.EmptyState(Icons.Book, Tokens.Accent.Emerald, "Nothing in the dictionary yet. Add the words it keeps getting wrong.");
            return;
        }

        if (transcriptions)
        {
            _transcriptionsView ??= new TranscriptionsView(_composition.Transcripts, () => KeyName);
            _sectionHost.Content = _transcriptionsView;
            _toolsHost.Child = _transcriptionsView.Tools;
        }
        else
        {
            _dictionaryView ??= new DictionaryView(_composition.Dictionary, _composition.Suggestions);
            _sectionHost.Content = _dictionaryView;
            _toolsHost.Child = _dictionaryView.Tools;
            _dictionaryView.SetCompact(_sectionHost.Bounds.Width is > 0 and < Tokens.Layout.CompactToolsBelow);
        }
    }

    private void FocusSearch()
    {
        if (_sectionHost.Content is TranscriptionsView t) t.FocusSearch();
        else if (_sectionHost.Content is DictionaryView d) d.FocusSearch();
    }

    /// <summary>Copies the most recent transcript. Also reachable from the tray.</summary>
    public async void CopyLast()
    {
        var records = _composition?.Transcripts.Records;
        if (records is not { Count: > 0 } || Clipboard is null) return;
        await Clipboard.SetTextAsync(records[0].Text).ConfigureAwait(true);
    }

    /// <summary>Shows settings.</summary>
    public void ShowSettings()
    {
        SetPage(dictations: false);
        _transcriptionsLink.IsActive = false;
        _dictionaryLink.IsActive = false;
        _settingsLink.IsActive = true;
        SetTitle("Your ", "settings");
        _toolsHost.Child = null;
        if (_composition is null)
        {
            _sectionHost.Content = Text.Body("Settings");
            return;
        }
        if (_settingsView is null)
        {
            _settingsView = new SettingsView(_composition);
            _settingsView.ModelChanged += (_, _) => ModelChanged();
        }
        _sectionHost.Content = _settingsView;
    }

    private void ShowAbout() => _ = new AboutWindow().ShowDialog(this);

    private void ShowFault(string message)
    {
        _faultMessage = message;
        _faultOnCard.Message = message;
        _faultOnTitle.Message = message;
        PlaceFault();
    }

    /// <summary>Clears the fault, as pressing its chip does.</summary>
    public void DismissFault()
    {
        _faultMessage = null;
        PlaceFault();
    }

    private static void OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(path) { UseShellExecute = true };
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"could not open {path}: {e.Message}");
        }
    }

    private string KeyName => KeyNames.Describe(_composition?.Settings.Data.PushToTalkKey ?? 0xA3, _composition?.Settings.Data.PushToTalkModifiers ?? 0);

    /// <summary>The line under the state: the shortcut as keycaps and how it works, or why it's paused.</summary>
    private void RefreshHint()
    {
        var paused = _composition is not null && !_composition.Settings.Data.IsEnabled;
        var mode = _composition?.Settings.Data.Mode switch
        {
            ActivationMode.Tap => "tap to start and stop",
            ActivationMode.Hold => "hold to talk",
            _ => "hold or tap to talk",
        };
        var hint = paused ? "paused" : $"{KeyName}|{mode}";
        if (hint == _lastHint) return;
        _lastHint = hint;

        if (paused)
        {
            _shortcut.Child = Text.Muted("Switch it back on to listen for your shortcut.");
            return;
        }

        var how = Text.Muted(mode);
        how.VerticalAlignment = VerticalAlignment.Center;
        _shortcut.Child = Panels.Row(Tokens.Space.Snug, KeyCaps.Make(KeyName), how);
        ToolTip.SetTip(_shortcut, _composition?.Settings.Data.AiCleanup == false
            ? "Instant: typed exactly as you said it."
            : "Polished: tidied by Gemini before it lands.");
    }

    /// <summary>Pulls state from the engine onto the status card.</summary>
    private void SyncFromEngine()
    {
        RefreshHint();
        var engine = _composition?.Engine;
        if (engine is null)
        {
            if (_startedAt is not null) UpdateCounter();
            return;
        }

        var recording = engine.State == DictationState.Recording;
        var transcribing = engine.State == DictationState.Transcribing;
        var busy = recording || transcribing;

        _bars.Level = Math.Clamp(engine.Level * Tokens.Layout.MainLevelGain, 0, 1);
        _bars.IsLive = recording;

        var state = recording ? "Listening" : transcribing ? "Working" : "Ready";
        if (state != _lastState)
        {
            _lastState = state;
            SetState(recording, transcribing);
        }

        if (busy && _startedAt is null) _startedAt = DateTimeOffset.Now;
        else if (!busy) _startedAt = null;
        UpdateCounter();

        App.SetTrayRecording(recording);

        var preview = engine.Preview;
        if (preview != _lastPreview)
        {
            _lastPreview = preview;
            _preview.Text = preview;
            _preview.IsVisible = preview.Length > 0;
        }

        if (_overlay is not null)
        {
            var cleaning = transcribing && _composition!.Settings.Data.AiCleanup;
            if (busy) { _overlay.Present(); _overlay.Sync(recording, transcribing, cleaning, engine.Level, _counter.Text ?? string.Empty, preview); }
            else if (_overlay.IsVisible && !_overlay.IsShowingTransient) _overlay.Dismiss();
        }
    }

    /// <summary>Pauses or resumes the key without quitting.</summary>
    private void SetEnabled(bool on)
    {
        if (_composition is not null && _composition.Settings.Data.IsEnabled != on)
        {
            _composition.Settings.Update(_composition.Settings.Data with { IsEnabled = on });
        }
        _lastState = string.Empty;
        _lastHint = string.Empty;
        RefreshHint();
        SetState(recording: false, transcribing: false);
    }

    private void SetState(bool recording, bool transcribing)
    {
        _counter.IsVisible = recording || transcribing;
        _working.IsVisible = transcribing;
        _bars.IsVisible = !transcribing;
        // The tile melts to its new hue and gives the toggles' small spring, so a change of state is felt.
        if (IsVisible) _stateTile.Pop();
        var off = _composition is not null && !_composition.Settings.Data.IsEnabled;
        if (off && !recording && !transcribing)
        {
            _stateTile.SetAccent(Tokens.Accent.Slate);
            _stateTile.SetIcon(Icons.MicOff);
            _stateTitle.Text = "Paused";
            _readoutLabel.Text = "OFF";
            return;
        }

        _stateTile.SetIcon(transcribing ? Icons.Sparkles : Icons.Mic);
        if (recording)
        {
            _stateTile.SetAccent(Tokens.Accent.Crimson);
            _stateTitle.Text = "Listening";
            _readoutLabel.Text = "RECORDING";
        }
        else if (transcribing)
        {
            _stateTile.SetAccent(Tokens.Accent.Amber);
            _stateTitle.Text = _composition?.Settings.Data.AiCleanup == false ? "Writing it out" : "Tidying up";
            _readoutLabel.Text = "TRANSCRIBING";
        }
        else
        {
            _stateTile.SetAccent(Tokens.Accent.Brand);
            _stateTitle.Text = "Ready";
            _readoutLabel.Text = "IDLE";
        }
    }

    private void UpdateCounter()
    {
        var elapsed = _startedAt is null ? TimeSpan.Zero : DateTimeOffset.Now - _startedAt.Value;
        _counter.Text = string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}");
    }

    /// <summary>Toggles the transport. Exposed for headless tests.</summary>
    public void ToggleRecording()
    {
        if (_composition?.Engine is null)
        {
            IsRecording = !IsRecording;
            _bars.IsLive = IsRecording;
            SetState(IsRecording, transcribing: false);
            _startedAt = IsRecording ? DateTimeOffset.Now : null;
            return;
        }

        _composition.Engine.TogglePushToTalk();
        SyncFromEngine();
    }

    /// <summary>Whether the transport is engaged. Exposed for headless tests.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>The state the readout reflects. Exposed for headless tests.</summary>
    public string StateText => _readoutLabel.Text switch { "RECORDING" => "Listening", "TRANSCRIBING" => "Working", _ => "Ready" };

    /// <summary>The bars. Exposed for headless tests.</summary>
    public LevelBars Bars => _bars;

    /// <summary>The fault notice. Exposed for headless tests.</summary>
    public FaultChip FaultNotice => _onDictations ? _faultOnCard : _faultOnTitle;

    /// <summary>Shows a fault. Exposed for headless tests.</summary>
    public void ReportFault(string message) => ShowFault(message);

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // An edit still waiting on its debounce must not be lost to the window going.
        _settingsView?.Flush();

        // Closing leaves the app in the tray; the hotkey still works. Quit is explicit.
        if (!App.IsQuitting && _composition is not null)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        _overlay?.Close();
        base.OnClosed(e);
    }
}

/// <summary>The smallest possible command, for key bindings.</summary>
public sealed class Relay : System.Windows.Input.ICommand
{
    private readonly Action _action;

    /// <summary>Wraps an action.</summary>
    public Relay(Action action) => _action = action;

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _action();
}
