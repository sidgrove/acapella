using Avalonia.Controls;
using Avalonia.Layout;
using Murmur.App.Controls;

namespace Murmur.AppTests.BibleAudit;

/// <summary>
/// C, "Two sections and a cog": Settings is set-up, not a place the work lives, so it leaves the
/// row of sections and becomes one marked button at the end of the title row (the Bible: the end
/// of the row "holds only the rare (hop-outs, downloads, setup, uploads)"). Two sections are left
/// to choose between, in the house section buttons.
/// </summary>
internal static class SectionNavC
{
    public const string Name = "C  Two sections and a cog";
    public const string Line = "Settings is set-up, so it leaves the sections and closes the title row as one marked button.";

    public static SectionGroup Build() => new(6,
        new HeldButton("Dictations", Icons.Mic, House.Brand, House.SectionButton),
        new HeldButton("Dictionary", Icons.Book, House.Green, House.SectionButton, waiting: 2));

    public static HeldButton BuildSettings() => new("Settings", Icons.Sliders, House.Purple, House.MarkedAction) { IsBare = true, IsTabStop = true };

    /// <summary>The whole option in one row, for the states sheet.</summary>
    public static (StackPanel Row, SectionGroup Sections, HeldButton Settings) BuildTogether()
    {
        var sections = Build();
        var settings = BuildSettings();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28, VerticalAlignment = VerticalAlignment.Center, Children = { sections, settings } };
        return (row, sections, settings);
    }

    public static void Place(Chrome chrome, int current = 0)
    {
        var sections = Build();
        sections.Choose(current);
        chrome.ReplaceTabs(sections);
        chrome.AddTool(BuildSettings());
        chrome.FoldIfTight(sections);
    }
}
