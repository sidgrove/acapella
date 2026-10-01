using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.Abstractions;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// The first-run walkthrough: get the model, pick the key, try it.
/// </summary>
/// <remarks>
/// Three steps and out. The playground is a real dictation into a real text box, so the
/// user has seen the whole loop work before they are on their own. The steps sit along the
/// top as tinted tiles, each ticking over to emerald once it's done.
/// </remarks>
public sealed class WelcomeWindow : ShellWindow
{
    private static readonly (string Icon, Tokens.Accent Accent, string Name, string Tip)[] Steps =
    [
        (Icons.Cpu, Tokens.Accent.Info, "Get the speech model", "Everything is transcribed on this machine. One download, then no internet needed."),
        (Icons.Keyboard, Tokens.Accent.Brand, "Choose your key", "Pick something you never use for typing. Right Ctrl is the safe default."),
        (Icons.Mic, Tokens.Accent.Crimson, "Try it", "Click in the box, then use your key and speak. Escape cancels a recording."),
    ];

    private readonly Composition _composition;
    private readonly ContentControl _host;
    private readonly SgButton _next;
    private readonly SgButton _back;
    private readonly ModelPart _model;
    private readonly KeyPart _key;
    private readonly TextBox _playground;
    private readonly TextBlock _playgroundHint;
    private readonly Control[] _cards;
    private readonly List<(IconTile Tile, TextBlock Name)> _track = [];
    private int _step;

    /// <summary>Raised after the model is downloaded.</summary>
    public event EventHandler? ModelChanged;

    /// <summary>Builds the walkthrough.</summary>
    public WelcomeWindow(Composition composition)
    {
        _composition = composition;

        Title = "Welcome";
        IsSheet = true;
        Width = Tokens.Layout.SettingsWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _model = new ModelPart(composition);
        _model.ModelChanged += (_, _) => { ModelChanged?.Invoke(this, EventArgs.Empty); RefreshButtons(); };
        _key = new KeyPart(composition);

        _playground = Field.Multiline("Press your key and say something. It will land here.");
        _playground.Width = double.NaN;
        _playgroundHint = Text.Muted(string.Empty);

        var greeting = new TextBlock
        {
            FontFamily = Tokens.Fonts.Serif,
            FontSize = Tokens.Fonts.Title,
            LineHeight = Tokens.Fonts.Title * 1.25,
            LetterSpacing = Tokens.Fonts.TitleTracking,
            Foreground = Tokens.Brushes.Ink,
            Inlines = [new Run("Welcome to "), new Run(AppPaths.ProductName) { FontStyle = FontStyle.Italic, Foreground = Tokens.Brushes.BrandStrong }],
        };

        var track = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Wide };
        for (var i = 0; i < Steps.Length; i++)
        {
            var tile = new IconTile(Steps[i].Icon, Steps[i].Accent, Tokens.Layout.TileRow);
            var name = Text.Label(Steps[i].Name);
            name.VerticalAlignment = VerticalAlignment.Center;
            _track.Add((tile, name));
            track.Children.Add(Panels.Row(Tokens.Space.Snug, tile, name));
        }

        _host = new ContentControl();
        _back = new SgButton("Back", SgButton.Kind.Ghost);
        _back.Click += (_, _) => Go(_step - 1);
        _next = new SgButton("Next", SgButton.Kind.Primary);
        _next.Click += (_, _) => { if (_step == 2) Finish(); else Go(_step + 1); };

        var skip = new SgButton("Skip for now", SgButton.Kind.Quiet, compact: true);
        skip.Click += (_, _) => Finish();

        var footer = Panels.Split(skip, Panels.Row(Tokens.Space.Snug, _back, _next));

        var body = Panels.Column(Tokens.Space.Wide, greeting, track, _host, footer);
        body.Margin = new Thickness(Tokens.Space.Wide, Tokens.Space.Snug, Tokens.Space.Wide, Tokens.Space.Wide);
        Content = Frame("Welcome", body);

        // Built once: a control may only ever have one parent, and the parts are shared
        // between steps.
        _cards =
        [
            Panels.SettingsCard(Steps[0].Icon, Steps[0].Accent, Steps[0].Name, Steps[0].Tip, _model),
            Panels.SettingsCard(Steps[1].Icon, Steps[1].Accent, Steps[1].Name, Steps[1].Tip, _key),
            Panels.SettingsCard(Steps[2].Icon, Steps[2].Accent, Steps[2].Name, Steps[2].Tip, Panels.Column(Tokens.Space.Base, _playground, _playgroundHint)),
        ];

        _composition.Engine?.Completed += OnCompleted;
        Closed += (_, _) => { if (_composition.Engine is { } e) e.Completed -= OnCompleted; };

        // On the playground step Escape means "cancel the recording", as the card says. A
        // sheet closes on Escape, and that closed the walkthrough mid-dictation without
        // marking it done, so it came straight back on the next launch. Tunnelled so it
        // runs before the sheet's own handler.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape && _step == 2) e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Go(0);
    }

    private void OnCompleted(object? sender, DictationResult result) => Dispatcher.UIThread.Post(() =>
    {
        if (_step != 2) return;
        _playgroundHint.Text = $"That took {result.ProcessingTime.TotalMilliseconds:0} ms after you let go. {AppPaths.ProductName} types into whatever has focus, so it works anywhere.";
    });

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, 2);
        _host.Content = _cards[_step];

        // Done steps tick over to emerald, the current one keeps its own hue, the rest wait in slate.
        for (var i = 0; i < _track.Count; i++)
        {
            var (tile, name) = _track[i];
            tile.SetAccent(i < _step ? Tokens.Accent.Emerald : i == _step ? Steps[i].Accent : Tokens.Accent.Slate);
            tile.SetIcon(i < _step ? Icons.Check : Steps[i].Icon);
            name.Foreground = i == _step ? Tokens.Brushes.Ink : Tokens.Brushes.Faint;
        }

        if (_step == 2) _playground.Focus();
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        _back.IsVisible = _step > 0;
        _next.Content = _step == 2 ? "Done" : "Next";
        _next.IsEnabled = _step != 0 || ModelPart.IsInstalled;
    }

    private void Finish()
    {
        _composition.Settings.Update(_composition.Settings.Data with { HasOnboarded = true });
        Close();
    }
}
