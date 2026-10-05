using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// Past dictations, newest first, under a heading for each day: searchable, each copyable
/// and deletable.
/// </summary>
/// <remarks>
/// <para>
/// Each card wears the mark of the app it went into, Claude's or Slack's own icon, so the
/// history reads as the user's day rather than a log (the Bible's joy moment: one real detail
/// that makes the page feel like their own world).
/// </para>
/// <para>
/// Rows show which dictionary corrections fired and whether the clean-up fell back. Without
/// that the dictionary is invisible and there is no way to tell a rule that works from one
/// that never matches.
/// </para>
/// </remarks>
public sealed class TranscriptionsView : UserControl
{
    private readonly TranscriptStore _store;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly WrapPanel _summary;
    private readonly SgButton _clear;
    private readonly Func<string>? _shortcut;

    /// <summary>
    /// Builds the view over <paramref name="store"/>. <paramref name="shortcut"/> names the key to
    /// press, for the empty state, which shows it as keycaps.
    /// </summary>
    public TranscriptionsView(TranscriptStore store, Func<string>? shortcut = null)
    {
        _store = store;
        _shortcut = shortcut;

        _search = Field.Search("Search");
        // Only a real change of query rebuilds the list: the box raises TextChanged once
        // as its template applies, with the same empty text, and that used to redo every
        // card at first show.
        var lastQuery = string.Empty;
        _search.TextChanged += (_, _) =>
        {
            var query = _search.Text ?? string.Empty;
            if (query == lastQuery) return;
            lastQuery = query;
            Refresh();
        };

        _search.Width = Tokens.Layout.SearchWidth;
        // A narrow window gives the day's chips the room rather than the search box.
        SizeChanged += (_, e) => _search.Width = e.NewSize.Width < Tokens.Layout.NarrowSearchBelow ? Tokens.Layout.SearchWidthNarrow : Tokens.Layout.SearchWidth;

        _list = new StackPanel { Spacing = 0, Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, 0, Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy) };
        _summary = new WrapPanel { ItemSpacing = Tokens.Space.Snug, LineSpacing = Tokens.Space.Tight, VerticalAlignment = VerticalAlignment.Center };

        // Two clicks to wipe the history, and the button says so. One click used to
        // rewrite the file on the spot with no way back.
        // A bare bin until it's pressed: rarely wanted, so it shouldn't take the toolbar's room.
        // Quiet at rest (red only when something is wrong); coral once it is armed.
        var clear = IconAction(Icons.Trash, "Clear the whole history");
        _clear = clear;
        var armed = false;
        void Disarm()
        {
            armed = false;
            clear.Variant = SgButton.Kind.Quiet;
            clear.Content = string.Empty;
            clear.Width = Tokens.Layout.ButtonHeightSmall;
            clear.Padding = new Thickness(0);
        }
        var disarm = new Avalonia.Threading.DispatcherTimer { Interval = Tokens.Motion.ConfirmWindow };
        disarm.Tick += (_, _) => { disarm.Stop(); Disarm(); };
        clear.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                clear.Variant = SgButton.Kind.Danger;
                clear.Content = "Click again to clear everything";
                clear.Width = double.NaN;
                clear.Padding = new Thickness(Tokens.Layout.ButtonPadXSmall, 0);
                disarm.Stop();
                disarm.Start();
                return;
            }
            disarm.Stop();
            Disarm();
            _store.Clear();
            Refresh();
        };

        var tools = Panels.Row(Tokens.Space.Snug, _search, clear);
        Content = new DockPanel
        {
            Children =
            {
                Panels.Docked(Gutter(Panels.Split(_summary, tools)), Dock.Top),
                // Padding inside the scroll viewer, not margin outside it: the viewer clips to its
                // bounds, and without room the cards' edges are cut off.
                new ScrollViewer { Margin = new Thickness(0, Tokens.Space.Roomy, 0, 0), Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
            },
        };

        // The store changes on the engine's thread when a dictation completes; the list
        // must only be touched on the UI thread. One new record is one new row at the
        // top: rebuilding all 850 cards measured 120-200 ms with the window hidden and up
        // to two seconds with it shown, and the paste of the next dictation waited on it.
        _store.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(OnStoreChanged);
        Refresh();
    }

    private Guid? _newestShown;
    private int _shownCount;

    /// <summary>
    /// How many cards are built at a time. Dave's history passed 3,000 dictations in a
    /// month; building a card for every one made opening the window wait on all of them.
    /// Search still looks through everything.
    /// </summary>
    public const int PageSize = 200;

    private IReadOnlyList<TranscriptRecord> _matches = [];
    private int _built;
    private DateTime? _lastDay;
    private SgButton? _more;

    private void OnStoreChanged()
    {
        var records = _store.Records;
        var filtering = !string.IsNullOrEmpty(_search.Text);
        if (!filtering && records.Count == _shownCount + 1 && _shownCount > 0 && records[0].Id != _newestShown && records[1].Id == _newestShown
            && Day(records[0]) == Day(records[1]) && _list.Children.Count > 1)
        {
            // Straight under the day's heading, which is already the newest day's.
            _list.Children.Insert(1, BuildRow(records[0]));
            Seal();
            _newestShown = records[0].Id;
            _shownCount = records.Count;
            RefreshSummary();
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Today's wait from key-up to the words in the field: the typical one and the slowest,
    /// or null before the first dictation of the day. The number every change to speed is
    /// judged by, where it can be seen without opening the log.
    /// </summary>
    public static string? LatencySummary(IReadOnlyList<TranscriptRecord> records, DateTime today)
    {
        var waits = records
            .Where(r => r.At.ToLocalTime().Date == today && r.ProcessingSeconds > 0)
            .Select(r => r.ProcessingSeconds)
            .OrderBy(s => s)
            .ToList();
        if (waits.Count == 0) return null;

        var typical = waits.Count % 2 == 1 ? waits[waits.Count / 2] : (waits[waits.Count / 2 - 1] + waits[waits.Count / 2]) / 2;
        return string.Create(CultureInfo.CurrentCulture, $"today {waits.Count} dictated, typical wait {typical:0.00} s, slowest {waits[^1]:0.0} s");
    }

    /// <summary>The day's figures as chips: how many today, and how long the typical wait was.</summary>
    private void RefreshSummary()
    {
        _summary.Children.Clear();
        // With nothing kept there is nothing to count, search or clear.
        _clear.IsVisible = _store.Records.Count > 0;
        _search.IsVisible = _store.Records.Count > 0;
        if (_store.Records.Count == 0) return;
        var today = DateTime.Today;
        var todays = _store.Records.Where(r => r.At.ToLocalTime().Date == today).ToList();
        var count = new Chip(todays.Count == 1 ? "1 today" : $"{todays.Count} today", Tokens.Accent.Brand, Icons.Mic);
        ToolTip.SetTip(count, $"{_store.Records.Count:N0} kept in all");
        _summary.Children.Add(count);

        var waits = todays.Where(r => r.ProcessingSeconds > 0).Select(r => r.ProcessingSeconds).Order().ToList();
        if (waits.Count > 0)
        {
            var typical = waits.Count % 2 == 1 ? waits[waits.Count / 2] : (waits[waits.Count / 2 - 1] + waits[waits.Count / 2]) / 2;
            var wait = new Chip(string.Create(CultureInfo.CurrentCulture, $"{typical:0.00} s typical wait"), Tokens.Accent.Emerald, Icons.Zap);
            ToolTip.SetTip(wait, LatencySummary(_store.Records, today));
            _summary.Children.Add(wait);
        }

        var words = todays.Sum(r => r.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        if (words > 0) _summary.Children.Add(new Chip(string.Create(CultureInfo.CurrentCulture, $"{words:N0} words"), Tokens.Accent.Plum, Icons.Pen));
    }

    private static Control Gutter(Control control)
    {
        control.Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, 0);
        return control;
    }

    /// <summary>Puts the caret in the search field.</summary>
    public void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }

    private static DateTime Day(TranscriptRecord record) => record.At.ToLocalTime().Date;

    private void Refresh()
    {
        _matches = _store.Search(_search.Text ?? string.Empty);

        _list.Children.Clear();
        RefreshSummary();
        _newestShown = _store.Records.Count > 0 ? _store.Records[0].Id : null;
        _shownCount = _store.Records.Count;
        _built = 0;
        _lastDay = null;
        _more = null;

        if (_matches.Count == 0)
        {
            _list.Children.Add(_store.Records.Count == 0
                ? FirstDictation()
                : Panels.EmptyState(Icons.Search, Tokens.Accent.Slate, "Nothing matches that. Try another word."));
            return;
        }

        BuildPage();
    }

    /// <summary>
    /// The empty history: the key to press, drawn as the key, and where the words will go. One
    /// quiet line in a card, the Bible's empty state, with the action itself as the picture.
    /// </summary>
    private Border FirstDictation()
    {
        var key = _shortcut?.Invoke() ?? "your shortcut";
        var hold = Text.Muted("Hold");
        var rest = Text.Muted("and speak. Your words land wherever you're typing, and here.");
        hold.VerticalAlignment = rest.VerticalAlignment = VerticalAlignment.Center;
        rest.TextWrapping = TextWrapping.Wrap;
        var line = new WrapPanel { ItemSpacing = Tokens.Space.Snug, LineSpacing = Tokens.Space.Tight, VerticalAlignment = VerticalAlignment.Center, Children = { hold, KeyCaps.Make(key), rest } };
        var row = Panels.Row(Tokens.Space.Base, new IconTile(Icons.Mic, Tokens.Accent.Brand), line);
        row.HorizontalAlignment = HorizontalAlignment.Center;
        var card = Card.Standard(row);
        card.Padding = new Thickness(Tokens.Space.Wide, Tokens.Space.Section);
        return card;
    }

    /// <summary>The next page of cards, carrying on under the last day's heading, and the button for more.</summary>
    private void BuildPage()
    {
        if (_more is not null) _list.Children.Remove(_more);
        var end = Math.Min(_matches.Count, _built + PageSize);
        for (var i = _built; i < end; i++)
        {
            var record = _matches[i];
            if (Day(record) != _lastDay)
            {
                _lastDay = Day(record);
                var day = _lastDay.Value;
                _list.Children.Add(DayHeading(day, _matches.Count(r => Day(r) == day), first: _list.Children.Count == 0));
            }
            _list.Children.Add(BuildRow(record));
        }
        _built = end;

        var left = _matches.Count - _built;
        if (left <= 0)
        {
            _more = null;
            Seal();
            return;
        }
        _more = new SgButton(string.Create(CultureInfo.CurrentCulture, $"Show {Math.Min(left, PageSize)} more"), SgButton.Kind.Ghost, compact: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, Tokens.Space.Snug, 0, 0),
        };
        ToolTip.SetTip(_more, string.Create(CultureInfo.CurrentCulture, $"{left:N0} older ones. Search looks through them all."));
        _more.Click += (_, _) => BuildPage();
        _list.Children.Add(_more);
        Seal();
    }

    /// <summary>
    /// Shapes each day as one card: the heading is its top band, the rows hang beneath it with a
    /// hairline between them, and the last row rounds the card off. Run after any change to the
    /// list, because a row's place in its day decides its edges.
    /// </summary>
    private void Seal()
    {
        var children = _list.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not Row row) continue;
            row.Shape(first: i == 0 || children[i - 1] is not Row, last: i + 1 >= children.Count || children[i + 1] is not Row);
        }
    }

    /// <summary>
    /// The day's band: "Today", "Yesterday" or "Tuesday 29/09" with its count, on the soft header
    /// tint at the top of the day's card (rules by weight: a tinted band marks a group).
    /// </summary>
    private static Border DayHeading(DateTime day, int count, bool first)
    {
        var today = DateTime.Today;
        var name = day == today ? "Today" : day == today.AddDays(-1) ? "Yesterday"
            : day.ToString(day.Year == today.Year ? "dddd dd/MM" : "dddd dd/MM/yyyy", CultureInfo.GetCultureInfo("en-GB"));
        var label = Text.Label(name);
        label.Foreground = Tokens.Brushes.Ink;
        var tally = Text.Caption(count == 1 ? "1 dictation" : $"{count} dictations");
        tally.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Background = Tokens.Brushes.Surface,
            BorderBrush = Tokens.Brushes.CardBorder,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Card, Tokens.Radius.Card, 0, 0),
            Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug + 1),
            Margin = new Thickness(0, first ? 0 : Tokens.Space.Wide, 0, 0),
            Child = Panels.Row(Tokens.Space.Snug, label, tally),
        };
    }

    /// <summary>
    /// One dictation: its app's mark, the words, one quiet line of where it went and how long it
    /// took, and the time on the right. Under the pointer or the keyboard the time gives way to
    /// the row's verbs (copy first), so a long list carries no column of identical buttons.
    /// </summary>
    private Row BuildRow(TranscriptRecord record)
    {
        var copy = new SgButton("Copy", SgButton.Kind.Quiet, compact: true, icon: Icons.Copy);
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(record.Text).ConfigureAwait(true);

            copy.Content = "Copied";
            await Task.Delay(Tokens.Motion.Confirmation).ConfigureAwait(true);
            copy.Content = "Copy";
        };

        var delete = IconAction(Icons.Trash, "Delete this dictation", danger: true);
        delete.Click += (_, _) => _store.Remove(record.Id);

        // Where it went and how long it took: one quiet line, figures before words.
        var meta = new WrapPanel { ItemSpacing = Tokens.Space.Snug, LineSpacing = Tokens.Space.Tight, VerticalAlignment = VerticalAlignment.Center };
        var where = record.App is { Length: > 0 } app ? $"{AppMark.DisplayName(app)}  ·  " : string.Empty;
        var line = Text.Meta(string.Create(CultureInfo.CurrentCulture, $"{where}{record.AudioSeconds:0.0} s spoken  ·  {record.ProcessingSeconds:0.00} s wait"));
        line.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(line, Route(record));
        meta.Children.Add(line);

        if (StyleChip(record.Style) is { } style) meta.Children.Add(style);
        if (record.CleanupFailed)
        {
            var fallback = new Chip("Typed as heard", Tokens.Accent.Amber, Icons.Alert);
            ToolTip.SetTip(fallback, "The clean-up didn't answer in time or rewrote too much, so your words were typed as heard.");
            meta.Children.Add(fallback);
        }
        if (record.Corrections is { Count: > 0 } corrections)
        {
            foreach (var correction in corrections)
            {
                var label = correction.Count > 1
                    ? $"{correction.From} → {correction.To} ×{correction.Count}"
                    : $"{correction.From} → {correction.To}";
                var fix = new Chip(label, Tokens.Accent.Emerald, Icons.Book);
                ToolTip.SetTip(fix, "Put right by your dictionary");
                meta.Children.Add(fix);
            }
        }

        // What the user changed after it was typed, and what the model heard when it
        // differs: the two ways to tell a bad result from the microphone from one from the
        // clean-up. Each opens under the words, on demand.
        var reveals = new StackPanel { Spacing = Tokens.Space.Snug };
        var actions = Panels.Row(Tokens.Space.Hair);

        if (record.EditedText is { Length: > 0 } edited)
        {
            meta.Children.Add(new Chip("You edited this", Tokens.Accent.Plum, Icons.Pen));
            var panel = Reveal("What you left it as", edited, Tokens.Accent.Plum);
            reveals.Children.Add(panel);
            var show = IconAction(Icons.Pen, "Show your edit");
            show.Click += (_, _) => panel.IsVisible = !panel.IsVisible;
            actions.Children.Add(show);
        }

        if (record.RawText is { Length: > 0 } raw && raw != record.Text)
        {
            var panel = Reveal("What was heard", raw, Tokens.Accent.Info, copy: true);
            reveals.Children.Add(panel);
            var show = IconAction(Icons.Ear, "Show what was heard");
            show.Click += (_, _) => panel.IsVisible = !panel.IsVisible;
            actions.Children.Add(show);
        }

        actions.Children.Add(delete);
        actions.Children.Add(copy);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.VerticalAlignment = VerticalAlignment.Top;

        var time = Text.Number(record.At.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), Tokens.Fonts.Small, Tokens.Brushes.Faint);
        time.FontWeight = FontWeight.SemiBold;
        time.HorizontalAlignment = HorizontalAlignment.Right;
        time.VerticalAlignment = VerticalAlignment.Top;
        time.Margin = new Thickness(0, Tokens.Space.Snug, Tokens.Space.Snug, 0);
        ToolTip.SetTip(time, record.At.ToLocalTime().ToString("dddd dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("en-GB")));

        var words = Text.Reading(record.Text);
        var body = Panels.Column(Tokens.Space.Snug, words, reveals, meta);
        Control mark = record.App is { Length: > 0 } name ? new AppMark(name) : new IconTile(Icons.Mic, Tokens.Accent.Brand);
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(Tokens.Space.Roomy, Tokens.Space.Roomy, 0, 0);

        // The time and the verbs share the right-hand corner, one showing at a time.
        var corner = new Panel { Children = { time, actions }, Margin = new Thickness(Tokens.Space.Base, Tokens.Space.Base - 2, Tokens.Space.Base, 0), MinWidth = Tokens.Layout.ButtonHeightSmall };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var divider = new Border
        {
            Height = Tokens.Border.Hairline,
            Background = Tokens.Brushes.Line,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Tokens.Space.Roomy + Tokens.Layout.Tile + Tokens.Space.Roomy, 0, Tokens.Space.Roomy, 0),
        };
        Grid.SetColumnSpan(divider, 3);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(corner, 2);
        body.Margin = new Thickness(Tokens.Space.Roomy, Tokens.Space.Roomy + 2, 0, Tokens.Space.Roomy);
        grid.Children.Add(divider);
        grid.Children.Add(mark);
        grid.Children.Add(body);
        grid.Children.Add(corner);

        var row = new Row(grid, divider);

        // Only the row under the pointer, or holding the keyboard, offers its verbs.
        void Offer(bool on)
        {
            actions.Opacity = on ? 1 : 0;
            actions.IsHitTestVisible = on;
            time.Opacity = on ? 0 : 1;
            row.Background = on ? Tokens.Brushes.RowHover : Tokens.Brushes.Card;
        }
        Offer(false);
        row.PointerEntered += (_, _) => Offer(true);
        row.PointerExited += (_, _) => Offer(row.IsKeyboardFocusWithin);
        row.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty) Offer(row.IsKeyboardFocusWithin || row.IsPointerOver);
        };
        return row;
    }

    /// <summary>A dictation's row: white, edged on the sides, its hairline above it unless it opens the day.</summary>
    private sealed class Row : Border
    {
        private readonly Border _divider;

        public Row(Grid content, Border divider)
        {
            Child = content;
            _divider = divider;
            Background = Tokens.Brushes.Card;
            BorderBrush = Tokens.Brushes.CardBorder;
            Transitions = [new Avalonia.Animation.BrushTransition { Property = BackgroundProperty, Duration = Tokens.Motion.Quick }];
            Shape(first: true, last: true);
        }

        /// <summary>Sets the edges for the row's place in its day.</summary>
        public void Shape(bool first, bool last)
        {
            _divider.IsVisible = !first;
            BorderThickness = new Thickness(Tokens.Border.Hairline, 0, Tokens.Border.Hairline, last ? Tokens.Border.Hairline : 0);
            CornerRadius = last ? new CornerRadius(0, 0, Tokens.Radius.Card, Tokens.Radius.Card) : default;
        }
    }

    /// <summary>A bare icon until hovered, with its words in the tooltip.</summary>
    private static SgButton IconAction(string icon, string tip, bool danger = false)
    {
        var button = new SgButton(string.Empty, danger ? SgButton.Kind.Danger : SgButton.Kind.Quiet, compact: true, icon: icon)
        {
            Width = Tokens.Layout.ButtonHeightSmall,
            Padding = new Thickness(0),
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>A soft panel holding another version of the words, hidden until asked for.</summary>
    private Border Reveal(string title, string text, Tokens.Accent accent, bool copy = false)
    {
        var heading = Text.Label(title);
        heading.Foreground = accent.Ink;
        Control top = heading;
        if (copy)
        {
            var button = new SgButton("Copy", SgButton.Kind.Quiet, compact: true, icon: Icons.Copy);
            button.Click += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null) await clipboard.SetTextAsync(text).ConfigureAwait(true);
            };
            top = Panels.Split(heading, button);
        }
        var words = Text.Muted(text);
        return new Border
        {
            Background = accent.Fill,
            CornerRadius = new CornerRadius(Tokens.Radius.Button),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            Child = Panels.Column(Tokens.Space.Tight, top, words),
            IsVisible = false,
        };
    }

    /// <summary>What the writing was laid out as, in the style's own hue.</summary>
    private static Chip? StyleChip(string? style)
    {
        if (style is not { Length: > 0 }) return null;
        var (accent, icon, tip) = style switch
        {
            "Chat" => (Tokens.Accent.Emerald, Icons.Send, "Written as a chat message: one message, no sign-off."),
            "Email" => (Tokens.Accent.Amber, Icons.File, "Written as an email: short paragraphs, greeting and sign-off on their own lines."),
            "Prompt" => (Tokens.Accent.Info, Icons.Sparkles, "Written as a prompt: file names and code kept as said."),
            "Document" => (Tokens.Accent.Plum, Icons.Pen, "Written as a document."),
            _ => (Tokens.Accent.Slate, Icons.Pen, $"Written as {style.ToLowerInvariant()}."),
        };
        var chip = new Chip(style, accent, icon);
        ToolTip.SetTip(chip, tip);
        return chip;
    }

    /// <summary>Which models heard and tidied it, for the tooltip on the timing.</summary>
    private static string Route(TranscriptRecord record)
    {
        var heard = record.TranscribedBy is { Length: > 0 } cloud ? $"Heard by {Friendly(cloud)} and Parakeet" : "Heard by Parakeet on this machine";
        var cleaned = record.CleanupFailed ? "typed as heard" : record.CleanedBy is { Length: > 0 } by ? $"tidied by {by}" : "typed as heard";
        return $"{heard}, {cleaned}. The wait is from letting go to the words landing.";
    }

    private static string Friendly(string model) => model switch
    {
        "scribe_v2_realtime" => "ElevenLabs Scribe",
        _ => model,
    };
}
