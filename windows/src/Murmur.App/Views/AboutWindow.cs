using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.Abstractions;
using Murmur.App.Controls;
using Murmur.App.Design;

namespace Murmur.App.Views;

/// <summary>The About box: what it is, and the credits it owes.</summary>
public sealed class AboutWindow : ShellWindow
{
    /// <summary>Builds the box.</summary>
    public AboutWindow()
    {
        Title = "About";
        IsSheet = true;
        Width = Tokens.Layout.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new SgButton("Done", SgButton.Kind.Primary) { HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();

        var name = new TextBlock
        {
            FontFamily = Tokens.Fonts.Serif,
            FontSize = Tokens.Fonts.Title,
            LineHeight = Tokens.Fonts.Title * 1.25,
            LetterSpacing = Tokens.Fonts.TitleTracking,
            Foreground = Tokens.Brushes.Ink,
            Inlines = [new Run(AppPaths.ProductName) { FontStyle = FontStyle.Italic, Foreground = Tokens.Brushes.BrandStrong }],
        };

        var body = Panels.Column(Tokens.Space.Roomy,
            Card.Standard(Panels.Column(Tokens.Space.Base,
                name,
                Text.Body("Push-to-talk dictation for Windows. Press your key and talk; the text lands wherever you were typing. Speech recognition runs on this machine; nothing leaves it unless you switch on the cloud or the clean-up."),
                Credit(Icons.Cpu, Tokens.Accent.Info, "Speech model", "NVIDIA Parakeet TDT 0.6B v2, converted to ONNX and quantised to int8 by the sherpa-onnx project. Model weights CC-BY-4.0, © NVIDIA, modified. sherpa-onnx Apache-2.0. ONNX Runtime MIT."),
                Credit(Icons.Sparkles, Tokens.Accent.Plum, "Interface", "Avalonia UI, MIT. DM Sans and Inter, SIL Open Font License. Built by Sidgrove."))),
            ok);
        body.Margin = new Thickness(Tokens.Space.Wide, Tokens.Space.Snug, Tokens.Space.Wide, Tokens.Space.Wide);

        Content = Frame("About", body);
    }

    private static Grid Credit(string icon, Tokens.Accent accent, string title, string detail)
    {
        var words = Panels.Column(Tokens.Space.Hair, Text.Label(title), Text.Muted(detail));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var tile = new IconTile(icon, accent, Tokens.Layout.TileRow) { VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(words, 1);
        words.Margin = new Thickness(Tokens.Space.Base, 0, 0, 0);
        grid.Children.Add(tile);
        grid.Children.Add(words);
        return grid;
    }
}
