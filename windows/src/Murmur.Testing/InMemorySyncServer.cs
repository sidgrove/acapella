using System.Text.Json;
using System.Text.Json.Nodes;
using Murmur.Abstractions;

namespace Murmur.Testing;

/// <summary>
/// Sidgrove Intelligence's sync routes in memory, behaving as docs/sync.md describes: one row
/// per (kind, key), last writer wins on <c>updatedAt</c>, tombstones kept, a cursor that pages
/// through rows in change order, and a losing push answered with the server's copy.
/// </summary>
/// <remarks>
/// Stored data has its properties re-ordered alphabetically, the way Postgres <c>jsonb</c>
/// does, so a client that hashes the bytes it was sent rather than its own canonical form
/// shows up as an endless push loop in tests rather than in production.
/// </remarks>
public sealed class InMemorySyncServer : ISyncServer, IManagedKeyServer
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Kind, string Key), Row> _rows = [];
    private long _sequence;
    private int _keyFetches;

    /// <summary>The most rows one reply carries.</summary>
    public int PageSize { get; set; } = 500;

    /// <summary>Every sync request received, in order.</summary>
    public List<SyncRequest> Requests { get; } = [];

    /// <summary>Recording files by key.</summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    /// <summary>How many recording uploads arrived.</summary>
    public int Uploads { get; private set; }

    /// <summary>Whether the token was revoked.</summary>
    public bool Revoked { get; private set; }

    /// <summary>The managed keys handed to a signed-in PC; null plays an older server without the route.</summary>
    public ManagedKeySet? Keys { get; set; }

    /// <summary>How many times the keys were asked for.</summary>
    public int KeyFetches => Volatile.Read(ref _keyFetches);

    /// <summary>When set, a keys call throws this instead of answering; sync calls are unaffected.</summary>
    public Func<Exception>? FailKeys { get; set; }

    /// <summary>When set, every call throws this instead of answering.</summary>
    public Func<Exception>? Fail { get; set; }

    /// <summary>The live rows (not tombstones), by kind and key.</summary>
    public IReadOnlyDictionary<(string Kind, string Key), JsonElement?> Live
    {
        get
        {
            lock (_gate) return _rows.Where(r => !r.Value.Change.Deleted).ToDictionary(r => r.Key, r => r.Value.Change.Data);
        }
    }

    /// <summary>Whether a tombstone is held for the key.</summary>
    public bool IsDeleted(string kind, string key)
    {
        lock (_gate) return _rows.TryGetValue((kind, key), out var row) && row.Change.Deleted;
    }

    /// <inheritdoc />
    public Task<SyncReply> SyncAsync(SyncRequest request, CancellationToken cancellationToken)
    {
        if (Fail is { } fail) throw fail();
        lock (_gate)
        {
            Requests.Add(request);
            var losers = new List<SyncChange>();
            foreach (var change in request.Changes)
            {
                var id = (change.Kind, change.Key);
                if (_rows.TryGetValue(id, out var held) && change.UpdatedAt <= held.Change.UpdatedAt)
                {
                    losers.Add(held.Change);
                    continue;
                }
                _rows[id] = new Row(++_sequence, change with { Data = Reorder(change.Data), Device = request.Device });
            }

            var page = _rows.Values.Where(r => r.Sequence > request.Cursor).OrderBy(r => r.Sequence).ToList();
            var taken = page.Take(PageSize).ToList();
            var cursor = taken.Count > 0 ? taken[^1].Sequence : request.Cursor;
            var changes = losers.Where(l => taken.All(t => t.Change.Kind != l.Kind || t.Change.Key != l.Key))
                .Concat(taken.Select(t => t.Change)).ToList();
            return Task.FromResult(new SyncReply { Cursor = cursor, More = page.Count > taken.Count, Changes = changes });
        }
    }

    /// <inheritdoc />
    public Task<Uri?> RecordingUrlAsync(string key, RecordingTransfer transfer, CancellationToken cancellationToken)
    {
        if (Fail is { } fail) throw fail();
        lock (_gate)
        {
            if (transfer == RecordingTransfer.Download && !Files.ContainsKey(key)) return Task.FromResult<Uri?>(null);
            return Task.FromResult<Uri?>(new Uri($"https://recordings.test/{key}?{(transfer == RecordingTransfer.Upload ? "put" : "get")}"));
        }
    }

    /// <inheritdoc />
    public Task UploadAsync(Uri url, byte[] wav, CancellationToken cancellationToken)
    {
        if (Fail is { } fail) throw fail();
        lock (_gate)
        {
            Files[url.AbsolutePath.TrimStart('/')] = wav;
            Uploads++;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        if (Fail is { } fail) throw fail();
        lock (_gate) return Task.FromResult(Files.TryGetValue(url.AbsolutePath.TrimStart('/'), out var wav) ? wav : null);
    }

    /// <inheritdoc />
    public Task RevokeAsync(CancellationToken cancellationToken)
    {
        if (Fail is { } fail) throw fail();
        Revoked = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ManagedKeySet?> KeysAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _keyFetches);
        if (FailKeys is { } failKeys) throw failKeys();
        return Task.FromResult(Keys);
    }

    private static JsonElement? Reorder(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } element) return data;
        var source = JsonNode.Parse(element.GetRawText())!.AsObject();
        var sorted = new JsonObject();
        foreach (var (name, value) in source.OrderBy(p => p.Key, StringComparer.Ordinal)) sorted[name] = value?.DeepClone();
        using var document = JsonDocument.Parse(sorted.ToJsonString());
        return document.RootElement.Clone();
    }

    private sealed record Row(long Sequence, SyncChange Change);
}
