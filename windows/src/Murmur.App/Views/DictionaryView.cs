using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;
using Murmur.Dictionary;

namespace Murmur.App.Views;

/// <summary>
/// The dictionary: add, edit, delete, search.
/// </summary>
/// <remarks>
/// <para>
/// One row per word it writes, with every way it has been misheard as a chip beside it, so
/// "git pull" shows git pool, get pole and gitpool together under it, and a long list reads
/// like an index rather than in the order it was taught. Both entry kinds live in the one
/// list: they are two shapes of the same idea.
/// </para>
/// <para>
/// Clicking a word or a chip opens it; switching an entry off and deleting it happen there,
/// so eighty rows don't each carry a switch and a delete button.
/// </para>
/// </remarks>
public sealed class DictionaryView : UserControl
{
    private readonly DictionaryFile _file;
    private readonly SuggestionStore? _suggestions;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly WrapPanel _summary;
    private readonly SearchToggle _searchToggle;
    private SgButton? _add;

    /// <summary>In a narrow window Add word keeps its plus and gives up its words.</summary>
    public void SetCompact(bool compact)
    {
        if (_add is null) return;
        _add.Content = compact ? string.Empty : "Add word";
        _add.Width = compact ? Tokens.Layout.ButtonHeightSmall : double.NaN;
        _add.Padding = compact ? new Thickness(0) : new Thickness(Tokens.Layout.ButtonPadXSmall, 0);
        ToolTip.SetTip(_add, compact ? "Add word (Ctrl+N)" : null);
    }

    /// <summary>Search, the "..." menu and Add word, for the window's title row.</summary>
    public Control Tools { get; }

    /// <summary>Builds the view over <paramref name="file"/>, with <paramref name="suggestions"/> from the user's edits shown above it.</summary>
    public DictionaryView(DictionaryFile file, SuggestionStore? suggestions = null)
    {
        _file = file;
        _suggestions = suggestions;

        _search = Field.Search("Search");
        _search.TextChanged += (_, _) => Refresh();
        _searchToggle = new SearchToggle(_search);

        var add = new SgButton("Add word", SgButton.Kind.Primary, compact: true, icon: Icons.Plus);
        add.Click += (_, _) => ShowEditor(null);

        var open = new SgButton("Edit dictionary.txt by hand", SgButton.Kind.Quiet, compact: true, icon: Icons.File);
        open.Click += (_, _) => OpenInEditor(_file.FilePath);
        // The page's one action, the search and the rare one, on the title row like the history's.
        add.Margin = new Thickness(Tokens.Space.Snug, 0, 0, 0);
        _add = add;
        Tools = Panels.Row(0, MoreMenu.Held(_searchToggle, MoreMenu.Make(open)), add);

        _list = new StackPanel { Spacing = Tokens.Space.Base, Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, 0, Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy) };
        _summary = new WrapPanel { ItemSpacing = Tokens.Space.Snug, LineSpacing = Tokens.Space.Tight, VerticalAlignment = VerticalAlignment.Center };

        Content = new DockPanel
        {
            Children =
            {
                new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
            },
        };

        _file.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        if (_suggestions is not null) _suggestions.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    /// <summary>Suggestions still waiting, leaving out any the dictionary has since gained by another route.</summary>
    public static IReadOnlyList<DictionarySuggestion> Pending(SuggestionStore suggestions, DictionaryFile file) =>
        [.. suggestions.Pending.Where(s => !file.Entries.Any(e => e.Kind == EntryKind.Correction && string.Equals(e.Hear.Trim(), s.Hear, StringComparison.OrdinalIgnoreCase)))];

    /// <summary>How many suggestions are waiting, for the tab's label.</summary>
    public static int PendingSuggestions(SuggestionStore suggestions, DictionaryFile file) => Pending(suggestions, file).Count;

    /// <summary>Puts the caret in the search field.</summary>
    public void FocusSearch()
    {
        _searchToggle.Open();
    }

    /// <summary>Opens the editor on a blank entry.</summary>
    public void AddEntry() => ShowEditor(null);

    /// <summary>The entries grouped by what they write, sorted as an index: the shape of the list.</summary>
    public static IReadOnlyList<(string Write, IReadOnlyList<DictionaryEntry> Entries)> Groups(IEnumerable<DictionaryEntry> entries) =>
        [.. entries
            .GroupBy(e => e.Write.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => ((g.FirstOrDefault(e => e.Kind == EntryKind.Term) ?? g.First()).Write.Trim(),
                (IReadOnlyList<DictionaryEntry>)[.. g.OrderBy(e => e.Kind).ThenBy(e => e.Hear, StringComparer.OrdinalIgnoreCase)]))];

    private void Refresh()
    {
        var groups = Groups(_file.Search(_search.Text ?? string.Empty));

        _list.Children.Clear();
        if (BuildSuggestions() is { } suggested) _list.Children.Add(suggested);
        if (BuildLearnt() is { } learnt) _list.Children.Add(learnt);

        var words = _file.Entries.Count(e => e.Kind == EntryKind.Term);
        var fixes = _file.Entries.Count - words;
        _summary.Children.Clear();
        _summary.Children.Add(new Chip(words == 1 ? "1 word" : string.Create(CultureInfo.CurrentCulture, $"{words:N0} words"), Tokens.Accent.Brand, Icons.Book));
        _summary.Children.Add(new Chip(fixes == 1 ? "1 correction" : string.Create(CultureInfo.CurrentCulture, $"{fixes:N0} corrections"), Tokens.Accent.Emerald, Icons.Check));

        if (groups.Count == 0)
        {
            _list.Children.Add(_file.Entries.Count == 0
                ? Panels.EmptyState(Icons.Book, Tokens.Accent.Emerald, "Nothing in the dictionary yet. Add the names and jargon it keeps getting wrong.")
                : Panels.EmptyState(Icons.Search, Tokens.Accent.Slate, "Nothing matches that. Try another word."));
            return;
        }

        // One panel, one row per word and a hairline between them: at eighty-odd entries a
        // card each made the list several screens long and hard to scan.
        var rows = new StackPanel();
        for (var i = 0; i < groups.Count; i++)
        {
            if (i > 0) rows.Children.Add(Rule());
            rows.Children.Add(BuildRow(groups[i].Write, groups[i].Entries));
        }

        // The counts ride in the list's own band, as today's figures do in the history: no strip.
        (_summary.Parent as Panel)?.Children.Remove(_summary);
        _summary.Name = "Counts";
        var title = Text.Label("A to Z");
        title.Foreground = Tokens.Brushes.Ink;
        var band = new Border
        {
            Background = Tokens.Brushes.Surface,
            BorderBrush = Tokens.Brushes.CardBorder,
            BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Card - 1, Tokens.Radius.Card - 1, 0, 0),
            Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug - 2, Tokens.Space.Base, Tokens.Space.Snug - 2),
            MinHeight = Tokens.Layout.DayBandHeight,
            Child = Panels.Split(title, _summary),
        };
        var card = Card.Standard(Panels.Column(0, band, new Border { Padding = new Thickness(Tokens.Space.Snug), Child = rows }), 0);
        card.Padding = new Thickness(0);
        _list.Children.Add(card);
    }

    private static Border Rule() => new() { Height = Tokens.Border.Hairline, Background = Tokens.Brushes.Line, Margin = new Thickness(Tokens.Space.Base, 0) };

    /// <summary>
    /// The fixes the user made by hand, offered as corrections, above the dictionary itself.
    /// Hidden while searching, so a search shows only what is already there.
    /// </summary>
    private Border? BuildSuggestions()
    {
        if (_suggestions is not { } store || !string.IsNullOrWhiteSpace(_search.Text)) return null;
        var pending = Pending(store, _file);
        if (pending.Count == 0) return null;

        var rows = new StackPanel
        {
            Children =
            {
                Pad(SectionHead.Make(Icons.Sparkles, Tokens.Accent.Amber, "Suggested from your edits",
                    "You fixed these by hand after they were typed. Add one and it's put right for you next time.",
                    new Chip(pending.Count.ToString(CultureInfo.CurrentCulture), Tokens.Accent.Amber))),
            },
        };
        foreach (var suggestion in pending)
        {
            rows.Children.Add(Rule());

            var detail = suggestion.Count > 1 ? $"fixed {suggestion.Count} times" : "fixed once";
            if (suggestion.Example is { Length: > 0 } example) detail = $"“{example}”  ·  {detail}";

            var dismiss = new SgButton("Not this", SgButton.Kind.Quiet, compact: true);
            dismiss.Click += (_, _) => store.Dismiss(suggestion.Id);
            var add = new SgButton("Add", SgButton.Kind.Primary, compact: true, icon: Icons.Plus);
            add.Click += (_, _) =>
            {
                _file.Add(DictionaryEntry.Correction(suggestion.Hear, suggestion.Write));
                store.Remove(suggestion.Id);
            };

            rows.Children.Add(Pad(Panels.Split(Fix(suggestion.Hear, suggestion.Write, detail), Panels.Row(Tokens.Space.Tight, dismiss, add))));
        }

        return Card.Standard(rows, Tokens.Space.Snug);
    }

    /// <summary>How long a fix added on its own stays listed with its Undo.</summary>
    public static readonly TimeSpan LearntShownFor = TimeSpan.FromDays(14);

    /// <summary>Fixes added on their own recently and still in the dictionary.</summary>
    public static IReadOnlyList<DictionarySuggestion> RecentlyLearnt(SuggestionStore suggestions, DictionaryFile file, DateTimeOffset now) =>
        [.. suggestions.Learnt.Where(s => now - s.LearntAt < LearntShownFor
            && file.Entries.Any(e => e.Kind == EntryKind.Correction && string.Equals(e.Hear.Trim(), s.Hear, StringComparison.OrdinalIgnoreCase)))];

    /// <summary>
    /// The fixes that went into the dictionary without the user adding them, each with an
    /// Undo, so nothing the app taught itself is hidden. Hidden while searching.
    /// </summary>
    private Border? BuildLearnt()
    {
        if (_suggestions is not { } store || !string.IsNullOrWhiteSpace(_search.Text)) return null;
        var learnt = RecentlyLearnt(store, _file, DateTimeOffset.Now);
        if (learnt.Count == 0) return null;

        var rows = new StackPanel
        {
            Children =
            {
                Pad(SectionHead.Make(Icons.Learn, Tokens.Accent.Emerald, "Learnt by itself",
                    "Added because Jev was sure they were mishearings, because you made the same fix twice, or because you fixed a word you hardly ever say. Undo one and it's never learnt again.")),
            },
        };
        foreach (var fix in learnt)
        {
            rows.Children.Add(Rule());

            var when = fix.LearntAt is { } at ? at.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture) : string.Empty;
            var detail = $"{when}  ·  {fix.LearntBecause}";
            if (fix.Example is { Length: > 0 } example) detail = $"“{example}”  ·  {detail}";

            var undo = new SgButton("Undo", SgButton.Kind.Quiet, compact: true, icon: Icons.Undo);
            undo.Click += (_, _) => EditLearner.Undo(fix, _file, store);

            rows.Children.Add(Pad(Panels.Split(Fix(fix.Hear, fix.Write, detail), undo)));
        }

        return Card.Standard(rows, Tokens.Space.Snug);
    }

    private static Border Pad(Control content) => new() { Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug), Child = content };

    /// <summary>A fix: what was heard, struck through, then what it becomes, and a line of where it came from.</summary>
    private static StackPanel Fix(string hear, string write, string detail)
    {
        var heard = Text.Muted(hear);
        heard.TextDecorations = TextDecorations.Strikethrough;
        var arrow = Text.Caption("→");
        arrow.VerticalAlignment = VerticalAlignment.Center;
        var caption = Text.Caption(detail);
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.TextWrapping = TextWrapping.NoWrap;
        return Panels.Column(Tokens.Space.Hair, Panels.Row(Tokens.Space.Snug, heard, arrow, Text.BodyStrong(write)), caption);
    }

    /// <summary>One word it writes, and every way it has been heard, as chips.</summary>
    private Border BuildRow(string write, IReadOnlyList<DictionaryEntry> entries)
    {
        var term = entries.FirstOrDefault(e => e.Kind == EntryKind.Term);
        var heard = entries.Where(e => e.Kind == EntryKind.Correction).ToList();
        var allOff = entries.All(e => !e.IsEnabled);

        var word = Text.BodyStrong(write);
        word.VerticalAlignment = VerticalAlignment.Center;
        word.Opacity = allOff ? Tokens.Opacity.Disabled : 1;

        var chips = new WrapPanel { ItemSpacing = Tokens.Space.Chip, LineSpacing = Tokens.Space.Chip, VerticalAlignment = VerticalAlignment.Center };
        foreach (var entry in heard)
        {
            var chip = new Chip(entry.Hear, Tokens.Accent.Slate)
            {
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Opacity = entry.IsEnabled ? 1 : Tokens.Opacity.Disabled,
            };
            ToolTip.SetTip(chip, entry.IsEnabled ? $"Heard as “{entry.Hear}”. Click to change it." : $"“{entry.Hear}” is switched off. Click to change it.");
            var target = entry;
            chip.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return;
                e.Handled = true;
                ShowEditor(target);
            };
            chips.Children.Add(chip);
        }
        if (entries.Any(e => !e.IsEnabled)) chips.Children.Add(new Chip("Off", Tokens.Accent.Amber));

        var left = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Tokens.Layout.WordColumn},*") };
        Grid.SetColumn(chips, 1);
        left.Children.Add(word);
        left.Children.Add(chips);

        var chevron = new Glyph(Icons.Pen, 13, Tokens.Brushes.Faint) { Opacity = 0 };
        var row = new Border
        {
            Background = Tokens.Brushes.None,
            CornerRadius = new CornerRadius(Tokens.Radius.Button),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            MinHeight = Tokens.Layout.ButtonHeightSmall + Tokens.Space.Snug,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = Panels.Split(left, chevron),
        };
        row.PointerEntered += (_, _) => { row.Background = Tokens.Brushes.Surface; chevron.Opacity = 1; };
        row.PointerExited += (_, _) => { row.Background = Tokens.Brushes.None; chevron.Opacity = 0; };

        // A chip opens its own correction; the rest of the row opens the word itself, or its
        // only correction when it has no entry of its own.
        row.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left) ShowEditor(term ?? entries[0]);
        };
        return row;
    }

    private void ShowEditor(DictionaryEntry? entry)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var editor = new DictionaryEditorWindow(entry);
        editor.Saved += (_, saved) => { if (entry is null) _file.Add(saved); else _file.Update(saved); };
        editor.Deleted += (_, id) => _file.Remove(id);
        _ = editor.ShowDialog(owner);
    }

    /// <summary>Opens the dictionary in the user's default text editor.</summary>
    private static void OpenInEditor(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
            }

            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"could not open {path}: {e.Message}");
        }
    }
}

/// <summary>Add, edit, switch off or delete one dictionary entry, with the false-positive warning shown live.</summary>
public sealed class DictionaryEditorWindow : ShellWindow
{
    private readonly Segmented _kind;
    private readonly TextBox _hear;
    private readonly TextBox _write;
    private readonly StackPanel _hearField;
    private readonly TextBlock _writeLabel;
    private readonly StackPanel _warnings;
    private readonly Controls.Switch _on;
    private readonly SgButton _save;
    private readonly Guid _id;

    private EntryKind _entryKind;

    /// <summary>Raised when the user saves.</summary>
    public event EventHandler<DictionaryEntry>? Saved;

    /// <summary>Raised with the entry's id when the user deletes it.</summary>
    public event EventHandler<Guid>? Deleted;

    /// <summary>Creates the editor for a new or existing entry.</summary>
    public DictionaryEditorWindow(DictionaryEntry? entry)
    {
        _id = entry?.Id ?? Guid.NewGuid();
        _entryKind = entry?.Kind ?? EntryKind.Term;

        Title = entry is null ? "New entry" : "Edit entry";
        IsSheet = true;
        Width = Tokens.Layout.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _kind = new Segmented(["A word to know", "A correction"], _entryKind == EntryKind.Correction ? 1 : 0);
        _kind.Selected += (_, i) => SetKind(i == 1 ? EntryKind.Correction : EntryKind.Term);

        _hear = Field.Text("what it hears, e.g. cloud code", entry?.Hear);
        _write = Field.Text("what to write, e.g. Claude Code", entry?.Write);
        _hear.TextChanged += (_, _) => Revalidate();
        _write.TextChanged += (_, _) => Revalidate();

        _hearField = Panels.Labelled("When it hears", _hear);
        _writeLabel = Text.Label("Word or phrase");
        _warnings = new StackPanel { Spacing = Tokens.Space.Snug };

        _on = new Controls.Switch { IsChecked = entry?.IsEnabled ?? true, VerticalAlignment = VerticalAlignment.Center };
        var onRow = Panels.Split(Text.Body("In use"), _on);

        var cancel = new SgButton("Cancel", SgButton.Kind.Ghost);
        cancel.Click += (_, _) => Close();
        _save = new SgButton("Save", SgButton.Kind.Primary, icon: Icons.Check);
        _save.Click += (_, _) => { if (IsValid) { Saved?.Invoke(this, Draft); Close(); } };

        var delete = new SgButton("Delete", SgButton.Kind.Danger, icon: Icons.Trash) { IsVisible = entry is not null };
        delete.Click += (_, _) => { Deleted?.Invoke(this, _id); Close(); };

        var buttons = Panels.Split(delete, Panels.Row(Tokens.Space.Snug, cancel, _save));

        var body = Panels.Column(Tokens.Space.Roomy,
            _kind,
            _hearField,
            Panels.Column(Tokens.Space.Chip, _writeLabel, _write),
            _warnings,
            onRow,
            buttons);
        body.Margin = new Thickness(Tokens.Space.Wide, Tokens.Space.Snug, Tokens.Space.Wide, Tokens.Space.Wide);

        Content = Frame(Title, body);
        SetKind(_entryKind);
    }

    private DictionaryEntry Draft => new()
    {
        Id = _id,
        Kind = _entryKind,
        Write = (_write.Text ?? string.Empty).Trim(),
        Hear = _entryKind == EntryKind.Correction ? (_hear.Text ?? string.Empty).Trim() : string.Empty,
        IsEnabled = _on.IsChecked == true,
    };

    private bool IsValid => Draft.Write.Length > 0 && (_entryKind == EntryKind.Term || Draft.Hear.Length > 0);

    private void SetKind(EntryKind kind)
    {
        _entryKind = kind;
        _hearField.IsVisible = kind == EntryKind.Correction;
        _writeLabel.Text = kind == EntryKind.Correction ? "Write instead" : "Word or phrase";
        Revalidate();
    }

    private void Revalidate()
    {
        _warnings.Children.Clear();
        foreach (var warning in DictionaryWarning.Check(Draft))
        {
            var text = Text.Body(warning.Message);
            text.Foreground = Tokens.Accent.Amber.Ink;
            text.VerticalAlignment = VerticalAlignment.Center;
            var line = Panels.Row(Tokens.Space.Snug, new Glyph(Icons.Alert, 15, Tokens.Accent.Amber.Ink), text);
            text.MaxWidth = Tokens.Layout.DialogWidth - 2 * Tokens.Space.Wide - 4 * Tokens.Space.Base;
            _warnings.Children.Add(new Border
            {
                Background = Tokens.Accent.Amber.Fill,
                CornerRadius = new CornerRadius(Tokens.Radius.Button),
                Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
                Child = line,
            });
        }

        _save.IsEnabled = IsValid;
    }
}
