using Murmur.App.Controls;

namespace Murmur.AppTests.BibleAudit;

/// <summary>
/// A, "House section buttons": the closest match to how Sidgrove Intelligence chooses the current
/// section today (<c>SegmentedPicker appearance="buttons"</c>, Dave's pick on the joy review of
/// 10/10/2026: "more colour, the icons and softer"). Separate soft squares beside the title, no
/// bed, each led by its own small mark; the current one white, lifted and bold.
/// </summary>
internal static class SectionNavA
{
    public const string Name = "A  House section buttons";
    public const string Line = "Separate soft squares beside the title, each wearing its mark. The web app's own control.";

    public static SectionGroup Build() => new(6,
        new HeldButton("Dictations", Icons.Mic, House.Brand, House.SectionButton),
        new HeldButton("Dictionary", Icons.Book, House.Green, House.SectionButton, waiting: 2),
        new HeldButton("Settings", Icons.Sliders, House.Purple, House.SectionButton));

    public static void Place(Chrome chrome, int current = 0)
    {
        var sections = Build();
        sections.Choose(current);
        chrome.ReplaceTabs(sections);
        chrome.FoldIfTight(sections);
    }
}
