using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Murmur.Abstractions;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;
using Murmur.Speech;

namespace Murmur.App.Views;

/// <summary>
/// The push-to-talk section: recorder, quick picks, warning, activation mode.
/// </summary>
/// <remarks>
/// Shared by Settings and the first-run walkthrough, so the two never drift apart.
/// </remarks>
public sealed class KeyPart : UserControl
{
    /// <summary>Quick picks, in recommendation order. Any other key can be recorded.</summary>
    private static readonly (int Key, string Label)[] Keys =
    [
        (0xA3, "Right Ctrl"),
        (0xA1, "Right Shift"),
        (0x14, "Caps Lock"),
        (0x7C, "F13"),
        (0x91, "Scroll Lock"),
    ];

    private static string? WarningFor(int key) => key switch
    {
        0xA1 => "Right Shift also fires when you type a capital with your right hand. Taps under half a second are ignored, but Right Ctrl is quieter.",
        0xA5 => "Right Alt is AltGr on many European layouts and will interfere with typing @, €, \\ and |.",
        _ when KeyNames.TypesACharacter(key) => $"{KeyNames.Describe(key)} also types a character. The key is passed through, so you will get that character as well as a recording.",
        _ => null,
    };

    private const string IdleHint = "Click here, then press a key or a combination like Ctrl + Shift + Space";

    private readonly Composition _composition;
    private readonly AppSettings _settings;
    private readonly Border _keyName;
    private readonly Border _keyWarning;
    private readonly TextBlock _keyWarningText;
    private readonly Border _status;
    private bool _capturing;

    /// <summary>Raised after a new key is saved.</summary>
    public event EventHandler? KeyChanged;

    /// <summary>Builds the section.</summary>
    public KeyPart(Composition composition)
    {
        _composition = composition;
        _settings = composition.Settings;

        _keyName = new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = KeyCaps.Make(KeyNames.Describe(_settings.Data.PushToTalkKey, _settings.Data.PushToTalkModifiers)) };
        _keyWarningText = Text.Body(string.Empty);
        _keyWarningText.Foreground = Tokens.Accent.Amber.Ink;
        _keyWarningText.VerticalAlignment = VerticalAlignment.Center;
        _keyWarning = new Border
        {
            Background = Tokens.Accent.Amber.Fill,
            CornerRadius = new CornerRadius(Tokens.Radius.Button),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            Child = Panels.Row(Tokens.Space.Snug, new Glyph(Icons.Alert, 15, Tokens.Accent.Amber.Ink), _keyWarningText),
            IsVisible = false,
        };
        _status = Pill.Brand("Record a key");

        var mode = new Segmented(["Hold or tap", "Hold to talk", "Tap to toggle"], (int)ModeIndex(_settings.Data.Mode));
        mode.Selected += (_, i) =>
        {
            var chosen = i switch { 1 => ActivationMode.Hold, 2 => ActivationMode.Tap, _ => ActivationMode.Automatic };
            if (_settings.Data.Mode != chosen) _settings.Update(_settings.Data with { Mode = chosen });
        };

        var picks = Panels.Row(Tokens.Space.Snug);
        foreach (var (key, label) in Keys)
        {
            var pick = new SgButton(label, SgButton.Kind.Ghost, compact: true);
            pick.Click += (_, _) => SelectKey(key, 0);
            picks.Children.Add(pick);
        }

        var pickLabel = Text.Caption("Or pick one");
        pickLabel.VerticalAlignment = VerticalAlignment.Center;
        picks.Children.Insert(0, pickLabel);

        var modeRow = Panels.Row(Tokens.Space.Snug, mode, Hint.Make("Hold or tap: hold the key and talk, or tap it to start and tap again to stop. Escape cancels a recording. The key is passed through, never swallowed, so it can't get stuck down."));

        Content = Panels.Column(Tokens.Space.Base,
            BuildKeyCapture(),
            picks,
            _keyWarning,
            modeRow);

        SelectKey(_settings.Data.PushToTalkKey, _settings.Data.PushToTalkModifiers, initial: true);
    }

    private static int ModeIndex(ActivationMode mode) => mode switch { ActivationMode.Hold => 1, ActivationMode.Tap => 2, _ => 0 };

    /// <summary>The recorder: click, press any key, done.</summary>
    private Border BuildKeyCapture()
    {
        var hint = Text.Muted(IdleHint);
        var box = Card.Subtle(Panels.Split(Panels.Column(Tokens.Space.Snug, _keyName, hint), _status), Tokens.Space.Roomy);
        box.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        box.Focusable = true;

        void Finish(bool saved)
        {
            _capturing = false;
            _composition.Engine?.CancelCapture();
            box.BorderBrush = Tokens.Brushes.PanelBorder;
            hint.Text = IdleHint;
            SetStatus(saved ? "Saved" : "Record a key", saved ? Tokens.Accent.Emerald : Tokens.Accent.Brand);
        }

        // Recorded by the keyboard hook, not by this window: the hook sees Win, Alt and
        // Ctrl combinations that a window never receives, and it swallows them while
        // recording so the shortcut does not also fire in Windows.
        box.PointerPressed += (_, _) =>
        {
            if (_composition.Engine is null) { hint.Text = "Recording needs the keyboard hook, which is not available here."; return; }
            _capturing = true;
            box.Focus();
            box.BorderBrush = Tokens.Brushes.FocusBorder;
            hint.Text = "Listening… press a key, or hold modifiers and press a key. Escape to cancel";
            SetStatus("Listening…", Tokens.Accent.Crimson);
            _composition.Engine.BeginCapture();
        };

        if (_composition.Engine is { } engine)
        {
            EventHandler<(int VirtualKey, int Modifiers)> onCaptured = (_, chord) => Dispatcher.UIThread.Post(() =>
            {
                if (!_capturing) return;
                Finish(saved: true);
                SelectKey(chord.VirtualKey, chord.Modifiers);
            });
            // Escape reaches the hook, not this window: while recording, the hook swallows
            // every key. It reports the cancel and the box goes back to idle.
            EventHandler onCancelled = (_, _) => Dispatcher.UIThread.Post(() => { if (_capturing) Finish(saved: false); });

            // Paired attach/detach, not a one-off subscription: the settings view is cached
            // and swapped in and out of the window, and after the first swap-out a
            // detach-only unsubscribe left the recorder listening to nothing.
            AttachedToVisualTree += (_, _) =>
            {
                engine.Captured += onCaptured;
                engine.CaptureCancelled += onCancelled;
            };
            DetachedFromVisualTree += (_, _) =>
            {
                engine.Captured -= onCaptured;
                engine.CaptureCancelled -= onCancelled;
                if (_capturing) Finish(saved: false);
            };
        }

        box.KeyDown += (_, e) =>
        {
            if (_capturing && e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; Finish(saved: false); }
        };

        // Not cancelled on focus loss within the window: pressing Alt or Win moves focus,
        // and that is exactly the moment a chord is being recorded. Leaving the whole
        // window is different — a capture left running would swallow the first key typed
        // into another app and make it the hotkey.
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window) window.Deactivated += OnWindowDeactivated;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window) window.Deactivated -= OnWindowDeactivated;
        };
        void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (_capturing) Finish(saved: false);
        }

        return box;
    }

    private void SetStatus(string text, Tokens.Accent accent)
    {
        _status.Background = accent.Fill;
        if (_status.Child is TextBlock label) { label.Text = text; label.Foreground = accent.Ink; }
    }

    private void SelectKey(int key, int modifiers, bool initial = false)
    {
        var text = modifiers == 0 ? WarningFor(key) : null;
        _keyName.Child = KeyCaps.Make(KeyNames.Describe(key, modifiers));
        if (!initial && (_settings.Data.PushToTalkKey != key || _settings.Data.PushToTalkModifiers != modifiers))
        {
            _settings.Update(_settings.Data with { PushToTalkKey = key, PushToTalkModifiers = modifiers });
            KeyChanged?.Invoke(this, EventArgs.Empty);
        }

        _keyWarningText.Text = text ?? string.Empty;
        _keyWarning.IsVisible = text is not null;
    }
}

/// <summary>The speech-model section: status, download with progress, open folder.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The token source lives for one download and is disposed in that download's finally; the owning window closing cancels any download in flight.")]
public sealed class ModelPart : UserControl
{
    private readonly Composition _composition;
    private readonly StatusDot _dot;
    private readonly TextBlock _status;
    private readonly TextBlock _detail;
    private readonly SgButton _download;
    private readonly Gauge _gauge;
    private readonly TextBlock _gaugeText;
    private CancellationTokenSource? _downloading;

    /// <summary>Raised after a download completes, so the engine can load it.</summary>
    public event EventHandler? ModelChanged;

    /// <summary>Whether the model files are on disk.</summary>
    public static bool IsInstalled => ParakeetTranscriber.Locate() is not null;

    /// <summary>Builds the section.</summary>
    public ModelPart(Composition composition)
    {
        _composition = composition;

        _dot = new StatusDot { VerticalAlignment = VerticalAlignment.Center };
        _status = Text.BodyStrong(string.Empty);
        _detail = Text.Meta(string.Empty);
        _download = new SgButton("Download the model", SgButton.Kind.Primary, compact: true, icon: Icons.Download);
        _download.Click += (_, _) => _ = ToggleDownloadAsync();
        _gauge = new Gauge { IsVisible = false };
        _gaugeText = Text.Caption(string.Empty);
        _gaugeText.IsVisible = false;

        var openFolder = new SgButton("Open its folder", SgButton.Kind.Quiet, compact: true, icon: Icons.Folder);
        openFolder.Click += (_, _) => OpenFolder(ModelDownloader.DefaultTarget);

        Content = Panels.Column(Tokens.Space.Base,
            Panels.Row(Tokens.Space.Snug, _dot, _status),
            _detail,
            Panels.Row(Tokens.Space.Snug, _download, openFolder),
            _gauge,
            _gaugeText);

        // Cancelled when the window goes, not when this control leaves the tree: the
        // settings view is swapped out whenever the user looks at another section, and a
        // 660 MB download that dies because someone clicked "Transcriptions" is a bug.
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window) window.Closed += OnWindowClosed;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window) window.Closed -= OnWindowClosed;
        };
        Refresh();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => _downloading?.Cancel();

    /// <summary>Whether a download is running, so the caller can say so somewhere visible.</summary>
    public bool IsDownloading => _downloading is not null;

    /// <summary>Re-reads the model state.</summary>
    public void Refresh()
    {
        var located = ParakeetTranscriber.Locate();
        var loaded = _composition.Transcriber?.IsReady == true;

        _dot.Fill = located is null ? Tokens.Brushes.AmberMid : loaded ? Tokens.Accent.Emerald.Ink : Tokens.Brushes.BrandMid;
        _status.Text = located is null ? "Parakeet isn't installed yet" : loaded ? "Parakeet is ready" : "Parakeet found, loading";

        var size = (ModelDownloader.ApproximateBytes / 1_000_000d).ToString("0", CultureInfo.CurrentCulture);
        _detail.Text = located is not null
            ? $"Loaded from {located}"
            : $"Windows has no built-in speech engine, so {AppPaths.ProductName} can't transcribe until the Parakeet model is downloaded: about {size} MB, once, from Hugging Face. It runs entirely on this machine afterwards.";

        _download.IsVisible = located is null || _downloading is not null;
    }

    private async Task ToggleDownloadAsync()
    {
        if (_downloading is not null)
        {
            await _downloading.CancelAsync().ConfigureAwait(true);
            return;
        }

        _downloading = new CancellationTokenSource();
        _download.Content = "Cancel";
        _gauge.IsVisible = true;
        _gaugeText.IsVisible = true;
        _gauge.Fraction = 0;

        var progress = new Progress<DownloadProgress>(p => Dispatcher.UIThread.Post(() =>
        {
            _gauge.Fraction = p.Fraction;
            var received = (p.BytesReceived / 1_000_000d).ToString("0.0", CultureInfo.CurrentCulture);
            var total = p.TotalBytes is { } t ? (t / 1_000_000d).ToString("0.0", CultureInfo.CurrentCulture) : "?";
            _gaugeText.Text = $"{p.File}  {received} / {total} MB  ({p.FileIndex + 1} of {p.FileCount})";
        }));

        try
        {
            using var downloader = new ModelDownloader();
            Log.Info("model download started");
            await downloader.DownloadAsync(ModelDownloader.DefaultTarget, progress, _downloading.Token).ConfigureAwait(true);
            Log.Info("model download complete");
            _gaugeText.Text = "Download complete";
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            _gaugeText.Text = "Cancelled";
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Log.Error("model download failed", e);
            _gaugeText.Text = $"Failed: {e.Message}";
        }
        finally
        {
            _downloading.Dispose();
            _downloading = null;
            _download.Content = "Download model";
            _gauge.IsVisible = false;
            Refresh();
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(path) { UseShellExecute = true };
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"could not open {path}: {e.Message}");
        }
    }
}
