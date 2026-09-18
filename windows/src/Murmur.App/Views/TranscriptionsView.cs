using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// Past transcriptions: searchable, each copyable and deletable.
/// </summary>
/// <remarks>
/// Rows show which dictionary corrections fired and whether the AI tier rewrote them.
/// Without that the dictionary is invisible and there is no way to tell a rule that works
/// from one that never matches.
/// </remarks>
public sealed class TranscriptionsView : UserControl
{
    private readonly TranscriptStore _store;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly TextBlock _count;

    /// <summary>Builds the view over <paramref name="store"/>.</summary>
    public TranscriptionsView(TranscriptStore store)
    {
        _store = store;

        _search = Field.Search("Search transcriptions");
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
        _search.MaxWidth = Tokens.Layout.ContentMaxWidth / 2;
        _search.HorizontalAlignment = HorizontalAlignment.Right;

        _list = new StackPanel { Spacing = Tokens.Space.Base, Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, 0, Tokens.Layout.ScrollGutter * 2, Tokens.Space.Roomy) };
        _count = Text.Caption(string.Empty);

        // Two clicks to wipe the history, and the button says so. One click used to
        // rewrite the file on the spot with no way back.
        var clear = new SgButton("Clear all", SgButton.Kind.Quiet, compact: true);
        var armed = false;
        var disarm = new Avalonia.Threading.DispatcherTimer { Interval = Tokens.Motion.ConfirmWindow };
        disarm.Tick += (_, _) => { disarm.Stop(); armed = false; clear.Content = "Clear all"; };
        clear.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                clear.Content = "Click again to clear everything";
                disarm.Stop();
                disarm.Start();
                return;
            }
            disarm.Stop();
            armed = false;
            clear.Content = "Clear all";
            _store.Clear();
            Refresh();
        };

        Content = new DockPanel
        {
            Children =
            {
                Panels.Docked(Gutter(Panels.Split(new Badge("Recent"), _search)), Dock.Top),
                Panels.Docked(Gutter(Panels.Split(_count, clear)), Dock.Bottom),
                // Padding inside the scroll viewer, not margin outside it: the viewer clips to its
                // bounds, and without room the cards' shadows are cut off.
                new ScrollViewer { Margin = new Thickness(0, Tokens.Space.Roomy, 0, Tokens.Space.Snug), Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
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

    private void OnStoreChanged()
    {
        var records = _store.Records;
        var filtering = !string.IsNullOrEmpty(_search.Text);
        if (!filtering && records.Count == _shownCount + 1 && _shownCount > 0 && records[0].Id != _newestShown && records[1].Id == _newestShown)
        {
            _list.Children.Insert(0, BuildRow(records[0]));
            _newestShown = records[0].Id;
            _shownCount = records.Count;
            _count.Text = CountText();
            return;
        }

        Refresh();
    }

    private string CountText() => $"{_store.Records.Count} recording{(_store.Records.Count == 1 ? "" : "s")}";

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

    private void Refresh()
    {
        var records = _store.Search(_search.Text ?? string.Empty);

        _list.Children.Clear();
        _count.Text = CountText();
        _newestShown = _store.Records.Count > 0 ? _store.Records[0].Id : null;
        _shownCount = _store.Records.Count;

        if (records.Count == 0)
        {
            _list.Children.Add(Panels.EmptyState(
                _store.Records.Count == 0 ? "🎙️" : "🔍",
                _store.Records.Count == 0 ? "No recordings yet" : "No matches",
                _store.Records.Count == 0 ? "Press the key and speak." : "Try a different search."));
            return;
        }

        foreach (var record in records) _list.Children.Add(BuildRow(record));
    }

    private Border BuildRow(TranscriptRecord record)
    {
        var copy = new SgButton("Copy", SgButton.Kind.Ghost, compact: true);
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(record.Text).ConfigureAwait(true);

            copy.Content = "Copied";
            await Task.Delay(Tokens.Motion.Confirmation).ConfigureAwait(true);
            copy.Content = "Copy";
        };

        var delete = new SgButton("Delete", SgButton.Kind.Danger, compact: true);
        delete.Click += (_, _) => _store.Remove(record.Id);

        var meta = new WrapPanel { ItemSpacing = Tokens.Space.Base, LineSpacing = Tokens.Space.Tight };
        meta.Children.Add(Text.Caption(record.At.ToLocalTime().ToString("HH:mm · d MMM", CultureInfo.CurrentCulture)));
        meta.Children.Add(Text.Caption($"{record.AudioSeconds:0.0}s spoken · {record.ProcessingSeconds * 1000:0} ms"));

        if (record.CleanedBy is { Length: > 0 } model) meta.Children.Add(Pill.Brand($"AI · {model}"));
        if (record.CleanupFailed) meta.Children.Add(Pill.Amber("AI fell back · local text typed"));

        // What the model heard, when it differs from what was typed. Without this there is no
        // way to tell whether a bad result came from the microphone or from the clean-up.
        var hasRaw = record.RawText is { Length: > 0 } && record.RawText != record.Text;
        var rawBlock = Text.Muted(record.RawText ?? string.Empty);
        rawBlock.IsVisible = false;
        var showRaw = new SgButton("Show what was heard", SgButton.Kind.Quiet, compact: true);
        showRaw.IsVisible = hasRaw;
        showRaw.Click += (_, _) =>
        {
            rawBlock.IsVisible = !rawBlock.IsVisible;
            showRaw.Content = rawBlock.IsVisible ? "Hide what was heard" : "Show what was heard";
        };
        var copyRaw = new SgButton("Copy raw", SgButton.Kind.Quiet, compact: true);
        copyRaw.IsVisible = hasRaw;
        copyRaw.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && record.RawText is not null) await clipboard.SetTextAsync(record.RawText).ConfigureAwait(true);
        };
        if (hasRaw)
        {
            meta.Children.Add(showRaw);
            meta.Children.Add(copyRaw);
        }

        if (record.Corrections is { Count: > 0 } corrections)
        {
            foreach (var correction in corrections)
            {
                var label = correction.Count > 1
                    ? $"{correction.From} → {correction.To} ×{correction.Count}"
                    : $"{correction.From} → {correction.To}";
                meta.Children.Add(Pill.Amber(label));
            }
        }

        var actions = Panels.Row(Tokens.Space.Tight, copy, delete);
        actions.VerticalAlignment = VerticalAlignment.Top;

        var body = Panels.Column(Tokens.Space.Roomy, Text.Reading(record.Text), rawBlock, Panels.Split(meta, actions));
        body.Margin = new Thickness(0, 0, Tokens.Space.Roomy, 0);

        var card = Card.Standard(body, Tokens.Space.Roomy);
        card.CornerRadius = new CornerRadius(Tokens.Radius.CardLarge);
        card.BorderBrush = Tokens.Brushes.Line;
        card.BoxShadow = Tokens.Shadow.Card;
        return card;
    }
}
