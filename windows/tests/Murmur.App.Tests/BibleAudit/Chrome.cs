using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Murmur.App.Controls;
using Murmur.App.Views;

namespace Murmur.AppTests.BibleAudit;

/// <summary>
/// Finds the pieces of the real main window, so an option can be stood in the shipping chrome
/// (the real title, the real status card, the real list) without a line of the app changing.
/// </summary>
internal sealed class Chrome
{
    private Chrome(MainWindow window, Grid masthead, Border tabs, Border tools, Border status, DockPanel body)
    {
        Window = window;
        Masthead = masthead;
        Tabs = tabs;
        Tools = tools;
        Status = status;
        Body = body;
    }

    /// <summary>The window.</summary>
    public MainWindow Window { get; }

    /// <summary>The title row: the title, the sections beside it, air, then the page's tools.</summary>
    public Grid Masthead { get; }

    /// <summary>Today's section tabs on their bed.</summary>
    public Border Tabs { get; }

    /// <summary>Where the page's own tools sit, at the row's far right.</summary>
    public Border Tools { get; }

    /// <summary>The status card.</summary>
    public Border Status { get; }

    /// <summary>The column the title row, the status card and the section stand in.</summary>
    public DockPanel Body { get; }

    /// <summary>The page title.</summary>
    public TextBlock Title => Masthead.Children.OfType<TextBlock>().First();

    /// <summary>Reads the shown window.</summary>
    public static Chrome Of(MainWindow window)
    {
        var tabs = window.GetVisualDescendants().OfType<NavLink>().First().GetVisualAncestors().OfType<Border>().First(b => b.Parent is Grid);
        var masthead = (Grid)tabs.Parent!;
        var tools = masthead.Children.OfType<Border>().First(b => Grid.GetColumn(b) == 3);
        var body = (DockPanel)masthead.Parent!;
        var status = body.Children.OfType<Border>().First();
        return new Chrome(window, masthead, tabs, tools, status, body);
    }

    /// <summary>Stands <paramref name="sections"/> where today's tabs are, beside the title.</summary>
    public void ReplaceTabs(Control sections)
    {
        sections.Margin = Tabs.Margin;
        Masthead.Children.Remove(Tabs);
        Grid.SetColumn(sections, 1);
        Masthead.Children.Add(sections);
    }

    /// <summary>
    /// Folds the sections that are not current to their marks when the title, every word and the
    /// page's tools cannot share the row: the shipping masthead's own rule, asked once after layout.
    /// </summary>
    public void FoldIfTight(SectionGroup sections)
    {
        Window.UpdateLayout();
        var wanted = Title.DesiredSize.Width + sections.DesiredSize.Width + Tools.DesiredSize.Width;
        if (wanted > Masthead.Bounds.Width) sections.Compact = true;
        Window.UpdateLayout();
    }

    /// <summary>Adds <paramref name="control"/> to the page's tools, after what is already there.</summary>
    public void AddTool(Control control)
    {
        var existing = Tools.Child;
        Tools.Child = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (existing is not null) row.Children.Add(existing);
        row.Children.Add(control);
        Tools.Child = row;
    }

    /// <summary>Docks <paramref name="row"/> across the column, straight under the title row.</summary>
    public void AddRowUnderTitle(Control row)
    {
        DockPanel.SetDock(row, Dock.Top);
        Body.Children.Insert(Body.Children.IndexOf(Masthead) + 1, row);
    }
}
