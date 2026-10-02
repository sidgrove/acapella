using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Core.Sync;

/// <summary>What one sync run did.</summary>
/// <param name="Pushed">Changes sent, tombstones included.</param>
/// <param name="Pulled">Rows that came back, this PC's own echoes included.</param>
/// <param name="Applied">Pulled rows that changed something here.</param>
/// <param name="Uploaded">Recordings sent.</param>
/// <param name="Downloaded">Recordings fetched.</param>
public sealed record SyncRunResult(int Pushed, int Pulled, int Applied, int Uploaded, int Downloaded)
{
    /// <summary>Whether the run changed anything on either side.</summary>
    public bool DidAnything => Pushed + Applied + Uploaded + Downloaded > 0;
}

/// <summary>
/// One PC's half of the sync in docs/sync.md: snapshot the stores, push what changed since
/// the last sync, page through what changed elsewhere and apply it through the stores' own
/// methods, so their <c>Changed</c> events fire and the UI and engine pick it up live.
/// </summary>
/// <remarks>
/// <para>
/// <b>Conflicts.</b> A pulled change overwrites the local copy unless the local copy changed
/// since the last sync. Then the local one is pushed, stamped with this run's start time, and
/// the server's last-writer-wins decides: a push that loses is answered in the same reply by
/// the server's copy, which is applied like any pulled change, so both sides land where the
/// server does.
/// </para>
/// <para>
/// <b>Echoes.</b> Everything pushed comes back in a later page. Its hash already matches the
/// one recorded at push time, so it is ignored. Anything the engine does apply is recorded
/// with the hash of exactly what was written, so the next snapshot sees no change either;
/// and <see cref="IsApplyingOnThisThread"/> lets a listener ignore the store events the
/// engine itself causes.
/// </para>
/// <para>Not thread-safe: one run at a time, which <see cref="SyncService"/> guarantees.</para>
/// </remarks>
public sealed class SyncEngine
{
    /// <summary>The most changes one call may carry (docs/sync.md).</summary>
    public const int MaxChangesPerCall = 500;

    /// <summary>The largest <c>data</c> the server accepts.</summary>
    public const int MaxDataBytes = 64 * 1024;

    /// <summary>The longest key the server accepts.</summary>
    public const int MaxKeyLength = 300;

    /// <summary>The largest recording the server accepts.</summary>
    public const long MaxRecordingBytes = 50L * 1024 * 1024;

    /// <summary>A guard against a server that always says there is more.</summary>
    public const int MaxCallsPerRun = 2000;

    [ThreadStatic]
    private static bool t_applying;

    private readonly ISyncServer _server;
    private readonly string _statePath;
    private readonly string _device;
    private readonly IClock _clock;
    private readonly RecordingArchive? _recordings;
    private readonly TranscriptStore _transcripts;
    private readonly ISyncKind[] _kinds;
    private readonly HashSet<string> _warnedSkipped = new(StringComparer.Ordinal);

    /// <summary>Syncs the given stores through <paramref name="server"/>.</summary>
    /// <param name="server">The server.</param>
    /// <param name="statePath">Where <c>sync-state.json</c> lives.</param>
    /// <param name="device">This PC's name, sent with every call.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="suggestions">The suggestions and learnt fixes.</param>
    /// <param name="transcripts">The history.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="recordings">The recording archive, or null to leave recordings out.</param>
    /// <param name="clock">The clock that stamps pushes; the system clock if null.</param>
    public SyncEngine(
        ISyncServer server, string statePath, string device,
        DictionaryFile dictionary, SuggestionStore suggestions, TranscriptStore transcripts, AppSettings settings,
        RecordingArchive? recordings, IClock? clock = null)
    {
        _server = server;
        _statePath = statePath;
        _device = device;
        _clock = clock ?? SystemClock.Instance;
        _recordings = recordings;
        _transcripts = transcripts;
        _kinds = [new SettingsKind(settings), new DictionaryKind(dictionary), new SuggestionKind(suggestions), new HistoryKind(transcripts)];
    }

    /// <summary>The default location of the state file.</summary>
    public static string DefaultStatePath => Path.Combine(AppPaths.Root, "sync-state.json");

    /// <summary>
    /// True while the engine is writing pulled changes into a store on the calling thread,
    /// so a store's <c>Changed</c> handler can tell sync's own writes from the user's.
    /// </summary>
    public static bool IsApplyingOnThisThread => t_applying;

    /// <summary>The changes in one call stay under this many bytes of data, well inside the 4 MB body limit.</summary>
    public int MaxBytesPerCall { get; init; } = 3 * 1024 * 1024;

    /// <summary>When the last run finished, from the state file, so it survives a restart.</summary>
    public DateTimeOffset? LastSyncedAt => SyncState.Load(_statePath).LastSyncedAt;

    /// <summary>Forgets everything agreed with the server, for signing out.</summary>
    public void Reset()
    {
        try
        {
            File.Delete(_statePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"sync: state could not be deleted: {e.Message}");
        }
    }

    /// <summary>
    /// One full run: push, pull until the server has nothing more, apply, then fetch any
    /// recordings that arrived. Network failures and <see cref="SyncUnauthorizedException"/>
    /// propagate; progress made before them is saved.
    /// </summary>
    public async Task<SyncRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var state = SyncState.Load(_statePath);
        var counts = new Counts();
        var calls = 0;

        // A PC's first sync takes what the server has before offering anything of its own,
        // so a fresh install's defaults do not overwrite settings every other PC agreed on.
        if (!state.Primed)
        {
            var local = Snapshot();
            bool more;
            do
            {
                var reply = await _server.SyncAsync(new SyncRequest { Cursor = state.Cursor, Device = _device }, cancellationToken).ConfigureAwait(false);
                counts.Pulled += reply.Changes.Count;
                counts.Applied += Apply(reply.Changes, state, local, pending: null);
                state.Cursor = reply.Cursor;
                state.Save(_statePath);
                more = reply.More;
            }
            while (more && ++calls < MaxCallsPerRun);
            state.Primed = true;
            state.Save(_statePath);
        }

        var snapshot = Snapshot();
        // Whole milliseconds, as the wire carries it, so the comparison with pulled rows is like for like.
        var now = _clock.Now.ToUniversalTime();
        var stamp = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
        var pending = new Dictionary<string, (SyncChange Change, SyncItem? Item)>(StringComparer.Ordinal);
        foreach (var (id, item) in snapshot)
        {
            if (state.Items.TryGetValue(id, out var hash))
            {
                if (hash == item.Hash) continue;
            }
            else if (item.Kind == SyncKinds.Settings && item.Json == SyncedSettings.Defaults)
            {
                // A fresh install that signs in before the PC with the real settings has
                // nothing to say about them yet; pushing its defaults would overwrite them.
                continue;
            }
            pending[id] = (Upsert(item, stamp), item);
        }
        foreach (var id in state.Items.Keys.ToList())
        {
            if (snapshot.ContainsKey(id)) continue;
            var (kind, key) = Split(id);
            if (kind == SyncKinds.Recording)
            {
                // A recording is never deleted elsewhere because a file went missing here;
                // the audio is the scarcest thing kept. One still downloading stays put.
                if (!state.PendingDownloads.ContainsKey(key)) state.Items.Remove(id);
                continue;
            }
            pending[id] = (new SyncChange { Kind = kind, Key = key, Deleted = true, UpdatedAt = stamp }, null);
        }

        var queue = new Queue<string>(pending.Keys.OrderBy(Order).ThenBy(id => id, StringComparer.Ordinal));
        do
        {
            var batch = await NextBatchAsync(queue, pending, state, counts, cancellationToken).ConfigureAwait(false);
            var reply = await _server.SyncAsync(new SyncRequest { Cursor = state.Cursor, Device = _device, Changes = batch }, cancellationToken).ConfigureAwait(false);

            foreach (var change in batch)
            {
                var id = SyncState.Id(change.Kind, change.Key);
                if (change.Deleted) state.Items.Remove(id);
                else if (pending.TryGetValue(id, out var sent) && sent.Item is { } item) state.Items[id] = item.Hash;
                pending.Remove(id);
            }
            counts.Pushed += batch.Count;
            counts.Pulled += reply.Changes.Count;
            counts.Applied += Apply(reply.Changes, state, snapshot, pending);
            state.Cursor = reply.Cursor;
            state.Save(_statePath);

            if (!reply.More && queue.Count == 0) break;
        }
        while (++calls < MaxCallsPerRun);

        await DownloadAsync(state, counts, cancellationToken).ConfigureAwait(false);

        state.LastSyncedAt = _clock.Now;
        state.Save(_statePath);
        return new SyncRunResult(counts.Pushed, counts.Pulled, counts.Applied, counts.Uploaded, counts.Downloaded);
    }

    /// <summary>Settings first, then the small kinds, history and recordings last: a slow first sync still brings the things that matter most first.</summary>
    private static int Order(string id) => Split(id).Kind switch
    {
        SyncKinds.Settings => 0,
        SyncKinds.Dictionary => 1,
        SyncKinds.Suggestion => 2,
        SyncKinds.History => 3,
        _ => 4,
    };

    /// <summary>Every item as this PC holds it now, by state key.</summary>
    private Dictionary<string, SyncItem> Snapshot()
    {
        var items = new Dictionary<string, SyncItem>(StringComparer.Ordinal);
        foreach (var kind in _kinds)
        {
            foreach (var item in kind.Snapshot()) Add(item);
        }

        if (_recordings is not null && Directory.Exists(_recordings.KeptFolder))
        {
            var files = new HashSet<string>(Directory.GetFiles(_recordings.KeptFolder, "*.wav").Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
            foreach (var record in _transcripts.Records)
            {
                if (!files.Contains(Path.GetFileName(_recordings.KeptPathFor(record.At)))) continue;
                Add(new SyncItem(SyncKinds.Recording, record.Id.ToString("D"),
                    JsonSerializer.Serialize(new RecordingData(record.At), SyncJsonContext.Default.RecordingData)));
            }
        }

        return items;

        void Add(SyncItem item)
        {
            if (item.Key.Length is 0 or > MaxKeyLength || item.Json.Length > MaxDataBytes)
            {
                // Logged once per session, not every five minutes.
                if (_warnedSkipped.Add(item.Id)) Log.Warn($"sync: left out a {item.Kind} item too large for the server ({item.Key.Length} character key, {item.Json.Length} bytes)");
                return;
            }
            items.TryAdd(item.Id, item);
        }
    }

    private static SyncChange Upsert(SyncItem item, DateTimeOffset stamp)
    {
        using var document = JsonDocument.Parse(item.Json);
        return new SyncChange { Kind = item.Kind, Key = item.Key, Data = document.RootElement.Clone(), UpdatedAt = stamp };
    }

    /// <summary>Takes the next call's worth of changes, uploading each recording before the item that announces it.</summary>
    private async Task<List<SyncChange>> NextBatchAsync(
        Queue<string> queue, Dictionary<string, (SyncChange Change, SyncItem? Item)> pending, SyncState state, Counts counts, CancellationToken cancellationToken)
    {
        var batch = new List<SyncChange>();
        long bytes = 0;
        while (batch.Count < MaxChangesPerCall && queue.TryPeek(out var id))
        {
            // Recording uploads that could not be done are dropped from the pending set.
            if (!pending.TryGetValue(id, out var next))
            {
                queue.Dequeue();
                continue;
            }

            var size = (next.Item?.Json.Length ?? 0) + next.Change.Key.Length + 128;
            if (batch.Count > 0 && bytes + size > MaxBytesPerCall) break;
            queue.Dequeue();

            if (next.Change.Kind == SyncKinds.Recording && !next.Change.Deleted && !await UploadAsync(next.Change.Key, next.Item!, state, cancellationToken).ConfigureAwait(false))
            {
                pending.Remove(id);
                continue;
            }
            if (next.Change.Kind == SyncKinds.Recording && !next.Change.Deleted) counts.Uploaded++;

            batch.Add(next.Change);
            bytes += size;
        }
        return batch;
    }

    /// <summary>Sends one recording. False when it cannot be sent and should not be announced.</summary>
    private async Task<bool> UploadAsync(string key, SyncItem item, SyncState state, CancellationToken cancellationToken)
    {
        var at = JsonSerializer.Deserialize(item.Json, SyncJsonContext.Default.RecordingData)!.At;
        var path = _recordings!.KeptPathFor(at);
        byte[] wav;
        try
        {
            if (new FileInfo(path).Length > MaxRecordingBytes)
            {
                Log.Warn($"sync: recording {Path.GetFileName(path)} is over 50 MB and stays on this PC");
                // Recorded as synced so it is not tried again every run.
                state.Items[item.Id] = item.Hash;
                return false;
            }
            wav = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"sync: recording {Path.GetFileName(path)} could not be read: {e.Message}");
            return false;
        }

        var url = await _server.RecordingUrlAsync(key, RecordingTransfer.Upload, cancellationToken).ConfigureAwait(false);
        if (url is null) return false;
        await _server.UploadAsync(url, wav, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Fetches the recordings other PCs announced that this one does not have.</summary>
    private async Task DownloadAsync(SyncState state, Counts counts, CancellationToken cancellationToken)
    {
        if (_recordings is null) return;
        foreach (var (key, at) in state.PendingDownloads.ToList())
        {
            var path = _recordings.KeptPathFor(at);
            if (!File.Exists(path))
            {
                var url = await _server.RecordingUrlAsync(key, RecordingTransfer.Download, cancellationToken).ConfigureAwait(false);
                var wav = url is null ? null : await _server.DownloadAsync(url, cancellationToken).ConfigureAwait(false);
                if (wav is null)
                {
                    Log.Warn($"sync: recording {Path.GetFileName(path)} is not on the server");
                }
                else
                {
                    try
                    {
                        Directory.CreateDirectory(_recordings.KeptFolder);
                        var temp = path + ".tmp";
                        await File.WriteAllBytesAsync(temp, wav, cancellationToken).ConfigureAwait(false);
                        File.Move(temp, path, overwrite: true);
                        counts.Downloaded++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // Left pending; the next run tries again.
                        Log.Warn($"sync: recording {Path.GetFileName(path)} could not be saved: {e.Message}");
                        continue;
                    }
                }
            }
            state.PendingDownloads.Remove(key);
            state.Save(_statePath);
        }
    }

    /// <summary>
    /// Applies one page of pulled rows and returns how many changed something. With
    /// <paramref name="pending"/> null (the first pull) a pulled row always wins.
    /// </summary>
    private int Apply(
        IReadOnlyList<SyncChange> changes, SyncState state, Dictionary<string, SyncItem> local,
        Dictionary<string, (SyncChange Change, SyncItem? Item)>? pending)
    {
        var upserts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var removals = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var applied = 0;

        foreach (var change in changes)
        {
            if (string.IsNullOrEmpty(change.Kind) || string.IsNullOrEmpty(change.Key)) continue;
            var id = SyncState.Id(change.Kind, change.Key);

            // Changed here since the last sync and not pushed yet: this PC's copy goes up in a
            // later call and the server's last-writer-wins decides. If it loses, the reply to
            // that call carries the server's copy, which by then is no longer pending and so
            // is applied below like any other pulled change.
            if (pending is not null && pending.ContainsKey(id)) continue;

            if (change.Deleted)
            {
                state.Items.Remove(id);
                if (change.Kind == SyncKinds.Recording)
                {
                    // Never deletes audio; just stops waiting for it.
                    state.PendingDownloads.Remove(change.Key);
                    continue;
                }
                if (!local.Remove(id)) continue;
                if (KindOf(change.Kind) is null) continue;
                Bucket(removals, change.Kind).Add(change.Key);
                applied++;
                continue;
            }

            if (change.Data is not { } data) continue;
            var json = Canonical(change.Kind, change.Key, data);
            if (json is null)
            {
                Log.Warn($"sync: ignored a {change.Kind} row that could not be read");
                continue;
            }
            var item = new SyncItem(change.Kind, change.Key, json);
            state.Items[id] = item.Hash;
            if (local.TryGetValue(id, out var held) && held.Hash == item.Hash) continue;
            local[id] = item;

            if (change.Kind == SyncKinds.Recording)
            {
                var at = JsonSerializer.Deserialize(json, SyncJsonContext.Default.RecordingData)!.At;
                if (_recordings is not null && !File.Exists(_recordings.KeptPathFor(at))) state.PendingDownloads[change.Key] = at;
                continue;
            }

            Bucket(upserts, change.Kind)[change.Key] = json;
            applied++;
        }

        t_applying = true;
        try
        {
            foreach (var kind in _kinds)
            {
                var up = upserts.TryGetValue(kind.Kind, out var u) ? u.Select(p => (p.Key, p.Value)).ToList() : [];
                var down = removals.TryGetValue(kind.Kind, out var r) ? r.ToList() : [];
                if (up.Count > 0 || down.Count > 0) kind.Apply(up, down);
            }
        }
        finally
        {
            t_applying = false;
        }
        return applied;
    }

    private string? Canonical(string kind, string key, JsonElement data)
    {
        if (kind == SyncKinds.Recording)
        {
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("at", out var at) || !at.TryGetDateTimeOffset(out var when)) return null;
            return JsonSerializer.Serialize(new RecordingData(when), SyncJsonContext.Default.RecordingData);
        }
        return KindOf(kind)?.Canonical(key, data);
    }

    private ISyncKind? KindOf(string kind) => Array.Find(_kinds, k => k.Kind == kind);

    private static TValue Bucket<TValue>(Dictionary<string, TValue> buckets, string kind) where TValue : new()
    {
        if (!buckets.TryGetValue(kind, out var bucket)) buckets[kind] = bucket = new TValue();
        return bucket;
    }

    private static (string Kind, string Key) Split(string id)
    {
        var bar = id.IndexOf('|', StringComparison.Ordinal);
        return (id[..bar], id[(bar + 1)..]);
    }

    private sealed class Counts
    {
        public int Pushed;
        public int Pulled;
        public int Applied;
        public int Uploaded;
        public int Downloaded;
    }
}
