using Murmur.App.Controls;

namespace Murmur.AppTests.BibleAudit;

/// <summary>
/// B, "Marks only": the words are taken away. The title already says which page this is ("Your
/// dictations"), so the tab beside it saying "Dictations" says it twice. Three small marks, the
/// current one white and lifted, each named in its tooltip and to a screen reader.
/// </summary>
internal static class SectionNavB
{
    public const string Name = "B  Marks only";
    public const string Line = "The words go: the title names the page once. Three marks, each named in its tooltip.";

    public static SectionGroup Build() => new(6,
        new HeldButton("Dictations", Icons.Mic, House.Brand, House.SectionButton),
        new HeldButton("Dictionary", Icons.Book, House.Green, House.SectionButton, waiting: 2, onCorner: true),
        new HeldButton("Settings", Icons.Sliders, House.Purple, House.SectionButton))
    { AlwaysBare = true };

    public static void Place(Chrome chrome, int current = 0)
    {
        var sections = Build();
        sections.Choose(current);
        chrome.ReplaceTabs(sections);
    }
}
