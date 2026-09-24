using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Murmur.Abstractions;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// The main window, in the site's voice: a hero with a badge, a serif headline and its
/// italic accent, the one pill button with its coin, and a readout card; then the history
/// or the dictionary as 20px cards.
/// </summary>
public sealed class MainWindow : ShellWindow
{
    private readonly Composition? _composition;
    private readonly Badge _badge;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _counter;
    private readonly TextBlock _readoutLabel;
    private readonly LevelBars _bars;
    private readonly NavLink _transcriptionsLink;
    private readonly NavLink _dictionaryLink;
    private NavLink? _settingsLink;
    private SettingsView? _settingsView;
    private readonly ContentControl _sectionHost;
    private readonly Border _fault;
    private readonly TextBlock _faultText;
    private readonly DispatcherTimer _poll;
    private readonly OverlayWindow? _overlay;
    private Controls.Switch? _enabled;
    private TextBlock? _enabledLabel;

    private TranscriptionsView? _transcriptionsView;
    private DictionaryView? _dictionaryView;
    private readonly TextBlock _preview;
    private DateTimeOffset? _startedAt;
    private string _lastState = string.Empty;
    private string _lastPreview = string.Empty;

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

        _badge = new Badge("Ready") { IsVisible = false };
        _subtitle = Text.Muted(string.Empty);

        _counter = Text.Number("00:00", Tokens.Fonts.CaptionTitle, Tokens.Brushes.Muted);
        _readoutLabel = Text.Eyebrow("Idle");
        _bars = new LevelBars(Tokens.Layout.BarsCountSmall, Tokens.Layout.BarsHeight) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _preview = Text.Muted(string.Empty);
        _preview.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _preview.MaxWidth = Tokens.Layout.OverlayPreviewWidth;
        _preview.IsVisible = false;


        _transcriptionsLink = new NavLink("Transcriptions") { IsActive = true };
        _dictionaryLink = new NavLink("Dictionary");
        _transcriptionsLink.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryLink.Click += (_, _) => ShowSection(transcriptions: false);

        _faultText = Text.Body(string.Empty);
        _faultText.Foreground = Tokens.Brushes.Rose;
        _fault = BuildFault();

        _sectionHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tokens.Motion.PanelPoll };
        _poll.Tick += (_, _) => SyncFromEngine();
        _poll.Start();

        if (_composition is not null) _overlay = new OverlayWindow(PlatformFactory.CreateWindowTweaks());

        Content = Frame(AppPaths.ProductName, BuildBody(), BuildModeSelector());
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
            engine.Faulted += (_, message) => Dispatcher.UIThread.Post(() => ShowFault(message));
            engine.Completed += (_, result) =>
            {
                if (result.CleanupFailed && !engine.IsFaultedRecently)
                {
                    Dispatcher.UIThread.Post(() => ShowFault("AI clean-up rewrote rather than tidied, so the local text was typed. Both are in the history."));
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

    private WrapPanel BuildNav()
    {
        var settings = new NavLink("Settings");
        _settingsLink = settings;
        settings.Click += (_, _) => ShowSettings();
        return new WrapPanel
        {
            Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy, Tokens.Layout.ScrollGutter * 2, 0),
            Children = { Panels.Row(Tokens.Space.Tight, _transcriptionsLink, _dictionaryLink, settings) },
        };
    }

    private StackPanel BuildModeSelector()
    {
        var instant = new NavLink("Instant");
        var polished = new NavLink("Polished");
        ToolTip.SetTip(instant, "Local transcription. Text is typed without AI clean-up.");
        ToolTip.SetTip(polished, "Gemini tidies the transcript before it is typed.");

        void Refresh()
        {
            var usePolished = _composition?.Settings.Data.AiCleanup == true;
            instant.IsActive = !usePolished;
            polished.IsActive = usePolished;
        }

        void Choose(bool usePolished)
        {
            if (_composition is null || _composition.Engine?.State != DictationState.Idle) return;
            if (_composition.Settings.Data.AiCleanup != usePolished)
                _composition.Settings.Update(_composition.Settings.Data with { AiCleanup = usePolished });
        }

        instant.IsEnabled = polished.IsEnabled = _composition is not null;
        instant.Click += (_, _) => Choose(false);
        polished.Click += (_, _) => Choose(true);
        if (_composition is not null)
            _composition.Settings.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();

        var label = Text.Muted("Dictation mode");
        label.VerticalAlignment = VerticalAlignment.Center;
        return Panels.Row(Tokens.Space.Tight, label, instant, polished);
    }

    private Border BuildBody()
    {
        var body = new DockPanel { ClipToBounds = false };
        body.Children.Add(Panels.Docked(BuildNav(), Dock.Top));
        body.Children.Add(Panels.Docked(BuildHero(), Dock.Top));
        body.Children.Add(Panels.Docked(_fault, Dock.Top));
        body.Children.Add(_sectionHost);
        return new Border { Child = body, MaxWidth = Tokens.Layout.MainContentMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Roomy) };
    }

    /// <summary>The hero: badge, headline, subtitle, the pill; and the readout card beside it.</summary>
    private Grid BuildHero()
    {
        _enabled = new Controls.Switch { IsChecked = _composition?.Settings.Data.IsEnabled ?? true, VerticalAlignment = VerticalAlignment.Center };
        _enabled.IsCheckedChanged += (_, _) => SetEnabled(_enabled.IsChecked == true);
        var onLabel = Text.Eyebrow("On");
        onLabel.VerticalAlignment = VerticalAlignment.Center;
        var badgeRow = Panels.Row(Tokens.Space.Base, _badge, _enabled, onLabel);
        _enabledLabel = (TextBlock)badgeRow.Children[2];

        _subtitle.Margin = new Thickness(Tokens.Space.Roomy, 0);
        _subtitle.VerticalAlignment = VerticalAlignment.Center;
        _bars.IsVisible = false;
        var timer = Card.Subtle(_counter, Tokens.Space.Snug);
        timer.Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug);
        var readout = Panels.Row(Tokens.Space.Base, _bars, timer);
        var hero = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy, Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy),
        };
        Grid.SetColumn(_subtitle, 1);
        Grid.SetColumn(readout, 2);
        Grid.SetRow(_preview, 1);
        Grid.SetColumnSpan(_preview, 3);
        _preview.MaxWidth = Tokens.Layout.ContentMaxWidth;
        _preview.Margin = new Thickness(0, Tokens.Space.Snug, 0, 0);
        hero.Children.Add(badgeRow);
        hero.Children.Add(_subtitle);
        hero.Children.Add(readout);
        hero.Children.Add(_preview);
        return hero;
    }

    private Border BuildFault()
    {
        var dismiss = new SgButton("Dismiss", SgButton.Kind.Quiet, compact: true);
        dismiss.Click += (_, _) => _fault.IsVisible = false;

        var notice = Card.Notice(Panels.Split(_faultText, dismiss), Tokens.Brushes.RoseLight, Tokens.Brushes.RoseTint);
        notice.IsVisible = false;
        notice.Margin = new Thickness(Tokens.Layout.ScrollGutter, 0, Tokens.Layout.ScrollGutter, Tokens.Space.Roomy);
        return notice;
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
        if (_settingsLink is not null) _settingsLink.IsActive = false;
        RefreshHint();
        _transcriptionsLink.IsActive = transcriptions;
        _dictionaryLink.IsActive = !transcriptions;

        if (_composition is null)
        {
            _sectionHost.Content = Panels.EmptyState("🎙️", transcriptions ? "No recordings yet" : "Dictionary is empty",
                transcriptions ? "Use your shortcut to start dictating." : "Add the words it keeps getting wrong.");
            return;
        }

        if (transcriptions)
        {
            _transcriptionsView ??= new TranscriptionsView(_composition.Transcripts);
            _sectionHost.Content = _transcriptionsView;
        }
        else
        {
            _dictionaryView ??= new DictionaryView(_composition.Dictionary);
            _sectionHost.Content = _dictionaryView;
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

    /// <summary>Shows settings beneath the persistent recording controls.</summary>
    public void ShowSettings()
    {
        _transcriptionsLink.IsActive = false;
        _dictionaryLink.IsActive = false;
        if (_settingsLink is not null) _settingsLink.IsActive = true;
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
        _faultText.Text = message;
        _fault.IsVisible = true;
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

    /// <summary>The idle subtitle: which key, which mode, whether AI is on.</summary>
    private void RefreshHint()
    {
        if (_composition is null) { _subtitle.Text = "Use your shortcut to start dictating."; return; }

        if (!_composition.Settings.Data.IsEnabled)
        {
            _subtitle.Text = "Paused. Flip the switch to listen for the key again.";
            ToolTip.SetTip(_subtitle, null);
            return;
        }

        var mode = _composition.Settings.Data.Mode switch
        {
            ActivationMode.Tap => $"{KeyName} · Tap to start / stop",
            ActivationMode.Hold => $"{KeyName} · Hold to talk",
            _ => $"{KeyName} · Hold or tap to talk",
        };
        var ai = _composition.Settings.Data.AiCleanup ? " Cleaned up by Gemini before it lands." : " Typed exactly as you said it.";
        _subtitle.Text = mode;
        ToolTip.SetTip(_subtitle, ai.Trim());
    }

    /// <summary>Pulls state from the engine onto the hero.</summary>
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
            else if (_overlay.IsVisible && !_overlay.IsShowingTransient) _overlay.Hide();
        }
    }

    /// <summary>Pauses or resumes the key without quitting.</summary>
    private void SetEnabled(bool on)
    {
        if (_enabledLabel is not null) _enabledLabel.Text = on ? "ON" : "OFF";
        if (_composition is not null && _composition.Settings.Data.IsEnabled != on)
        {
            _composition.Settings.Update(_composition.Settings.Data with { IsEnabled = on });
        }
        _lastState = string.Empty;
        SetState(recording: false, transcribing: false);
    }

    private void SetState(bool recording, bool transcribing)
    {
        _bars.IsVisible = recording;
        var off = _composition is not null && !_composition.Settings.Data.IsEnabled;
        // The badge shows while busy, and while paused: an app that has been switched off
        // and looks exactly like one that is ready is a support question waiting to happen.
        _badge.IsVisible = recording || transcribing || off;
        if (off && !recording && !transcribing)
        {
            _badge.Set("Paused", Tokens.Brushes.Faint, live: false);
            _readoutLabel.Text = "OFF";
            return;
        }

        if (recording)
        {
            _badge.Set("Listening", Tokens.Brushes.Rose, live: true);
            _readoutLabel.Text = "RECORDING";
        }
        else if (transcribing)
        {
            _badge.Set("Working", Tokens.Brushes.AmberMid, live: true);
            _readoutLabel.Text = "TRANSCRIBING";
        }
        else
        {
            _badge.Set("Ready", Tokens.Brushes.Brand, live: false);
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
    public Border FaultNotice => _fault;

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
