using System.Text.Json;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Core.Sync;
using Murmur.Dictionary;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>A clock a test sets by hand, one per pretend PC.</summary>
internal sealed class SettableClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; set; } = start;
}

/// <summary>One pretend PC: its own folder, stores and sync engine, against a shared server.</summary>
internal sealed class Pc : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    public Pc(ISyncServer server, string name, DateTimeOffset? start = null)
    {
        Name = name;
        Folder = Path.Combine(Path.GetTempPath(), $"acapella-sync-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Folder);
        Clock = new SettableClock(start ?? Start);
        Settings = new AppSettings(Path.Combine(Folder, "settings.json"));
        Dictionary = new DictionaryFile(Path.Combine(Folder, "dictionary.txt"));
        Suggestions = new SuggestionStore(Path.Combine(Folder, "dictionary-suggestions.json"));
        History = new TranscriptStore(Path.Combine(Folder, "transcripts.jsonl"));
        Archive = new RecordingArchive(Path.Combine(Folder, "recordings"));
        StatePath = Path.Combine(Folder, "sync-state.json");
        Engine = new SyncEngine(server, StatePath, name, Dictionary, Suggestions, History, Settings, Archive, Clock);
    }

    public string Name { get; }
    public string Folder { get; }
    public string StatePath { get; }
    public SettableClock Clock { get; }
    public AppSettings Settings { get; }
    public DictionaryFile Dictionary { get; }
    public SuggestionStore Suggestions { get; }
    public TranscriptStore History { get; }
    public RecordingArchive Archive { get; }
    public SyncEngine Engine { get; }

    /// <summary>Moves this PC's clock on and syncs.</summary>
    public Task<SyncRunResult> SyncAsync(int afterSeconds = 1)
    {
        Clock.Now += TimeSpan.FromSeconds(afterSeconds);
        return Engine.RunAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

/// <summary>
/// Cross-PC sync (docs/sync.md) against an in-memory server with the real server's rules:
/// last writer wins, tombstones, paging and the losing push answered with the server's copy.
/// </summary>
public sealed class SyncEngineTests : IDisposable
{
    private readonly InMemorySyncServer _server = new();
    private readonly List<Pc> _pcs = [];

    public SyncEngineTests()
    {
        Log.Path = Path.Combine(Path.GetTempPath(), $"murmur-tests-{Environment.ProcessId}.log");
    }

    public void Dispose()
    {
        foreach (var pc in _pcs) pc.Dispose();
    }

    private Pc NewPc(string name, DateTimeOffset? start = null)
    {
        var pc = new Pc(_server, name, start);
        _pcs.Add(pc);
        return pc;
    }

    private static TranscriptRecord Record(string text, int minute) =>
        new() { At = Pc.Start.AddMinutes(minute), Text = text, RawText = text, AudioSeconds = 2.5, ProcessingSeconds = 0.4 };

    [Fact]
    public async Task A_first_sync_pushes_everything_and_a_second_pushes_nothing()
    {
        var a = NewPc("A");
        a.Dictionary.Add(DictionaryEntry.Term("Sidgrove"));
        a.Dictionary.Add(DictionaryEntry.Correction("Quilla", "Quiller"));
        a.History.Add(Record("Hello there", 1));
        a.Settings.Update(a.Settings.Data with { SendWord = "send" });

        var first = await a.SyncAsync();
        // Settings, two dictionary entries and one history record.
        first.Pushed.ShouldBe(4);
        _server.Live.Keys.ShouldContain((SyncKinds.Dictionary, "Term|Sidgrove"));
        _server.Live.Keys.ShouldContain((SyncKinds.Dictionary, "Correction|quilla|Quiller"));
        _server.Live.Keys.ShouldContain((SyncKinds.Settings, SyncKinds.SettingsKey));

        var second = await a.SyncAsync();
        second.Pushed.ShouldBe(0);
        second.Applied.ShouldBe(0);
    }

    [Fact]
    public async Task An_edit_pushes_one_change_and_a_delete_pushes_a_tombstone()
    {
        var a = NewPc("A");
        a.Dictionary.Add(DictionaryEntry.Term("Sidgrove"));
        a.Dictionary.Add(DictionaryEntry.Correction("Quilla", "Quiller"));
        await a.SyncAsync();

        // Switching an entry off is an edit of the same key: the flag lives in the line.
        var quilla = a.Dictionary.Entries.Single(e => e.Kind == EntryKind.Correction);
        a.Dictionary.Update(quilla with { IsEnabled = false });
        var edited = await a.SyncAsync();
        edited.Pushed.ShouldBe(1);
        _server.Requests[^1].Changes.Single().Key.ShouldBe("Correction|quilla|Quiller");
        _server.Live[(SyncKinds.Dictionary, "Correction|quilla|Quiller")]!.Value.GetProperty("line").GetString().ShouldBe("# off: Quilla -> Quiller");

        a.Dictionary.Remove(a.Dictionary.Entries.Single(e => e.Kind == EntryKind.Term).Id);
        var deleted = await a.SyncAsync();
        deleted.Pushed.ShouldBe(1);
        _server.Requests[^1].Changes.Single().Deleted.ShouldBeTrue();
        _server.IsDeleted(SyncKinds.Dictionary, "Term|Sidgrove").ShouldBeTrue();

        (await a.SyncAsync()).Pushed.ShouldBe(0);
    }

    [Fact]
    public async Task A_dictionary_entry_learnt_on_one_pc_arrives_on_the_other_and_a_delete_follows()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        await a.SyncAsync();
        await b.SyncAsync();

        a.Dictionary.Add(DictionaryEntry.Correction("Gev", "Jev"));
        await a.SyncAsync();
        var arrived = await b.SyncAsync();

        arrived.Applied.ShouldBe(1);
        b.Dictionary.Entries.ShouldContain(e => e.Kind == EntryKind.Correction && e.Hear == "Gev" && e.Write == "Jev");
        File.ReadAllText(b.Dictionary.FilePath).ShouldContain("Gev -> Jev");

        a.Dictionary.Remove(a.Dictionary.Entries.Single().Id);
        await a.SyncAsync();
        await b.SyncAsync();

        b.Dictionary.Entries.ShouldBeEmpty();
        // And B, having applied the delete, has nothing of its own to push back.
        (await b.SyncAsync()).Pushed.ShouldBe(0);
    }

    [Fact]
    public async Task Suggestions_history_and_settings_arrive_through_the_stores_events()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        await b.SyncAsync();

        var suggestion = a.Suggestions.Offer("Hoxon", "Hoxton", "…coffee in Hoxton…", Pc.Start);
        a.Suggestions.MarkLearnt(suggestion.Id, Pc.Start, "you made this fix 2 times");
        var record = Record("Can we run Impeccable over the serif headings?", 3) with { EditChecked = true, EditedText = "Can we run Impeccable over the serif headings please?", App = "claude" };
        a.History.Add(record);
        a.Settings.Update(a.Settings.Data with { SendWord = "send", GeminiApiKey = "gem-key", CustomInstructions = "No comma before and" });
        await a.SyncAsync();

        int dictionaryEvents = 0, suggestionEvents = 0, historyEvents = 0, settingsEvents = 0;
        b.Dictionary.Changed += (_, _) => dictionaryEvents++;
        b.Suggestions.Changed += (_, _) => suggestionEvents++;
        b.History.Changed += (_, _) => historyEvents++;
        b.Settings.Changed += (_, _) => settingsEvents++;

        await b.SyncAsync();

        b.Suggestions.Learnt.Single().ShouldBe(a.Suggestions.Learnt.Single());
        b.History.Records.Single().ShouldBeEquivalentTo(record);
        b.Settings.Data.SendWord.ShouldBe("send");
        b.Settings.Data.GeminiApiKey.ShouldBe("gem-key");
        b.Settings.Data.CustomInstructions.ShouldBe("No comma before and");
        suggestionEvents.ShouldBe(1);
        historyEvents.ShouldBe(1);
        settingsEvents.ShouldBe(1);
        dictionaryEvents.ShouldBe(0);

        // On disk too, so a restart keeps them.
        new TranscriptStore(Path.Combine(b.Folder, "transcripts.jsonl")).Records.Single().Id.ShouldBe(record.Id);
        new AppSettings(Path.Combine(b.Folder, "settings.json")).Data.SendWord.ShouldBe("send");
    }

    [Fact]
    public async Task Settings_keep_this_pcs_machine_fields()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        b.Settings.Update(b.Settings.Data with
        {
            MicrophoneDeviceId = "b-mic", ModelDirectory = @"D:\models", PushToTalkKey = 0xA5, PushToTalkModifiers = 2,
            HasOnboarded = true, IsEnabled = false, SyncEnabled = true, SyncServer = "https://staging.test",
        });
        await b.SyncAsync();

        a.Settings.Update(a.Settings.Data with
        {
            MicrophoneDeviceId = "a-mic", ModelDirectory = @"C:\elsewhere", PushToTalkKey = 0xA3, IsEnabled = true,
            SyncEnabled = false, HasOnboarded = false, RemoveFillers = false, JevApiKey = "jev",
        });
        await a.SyncAsync();
        await b.SyncAsync();

        var data = b.Settings.Data;
        data.RemoveFillers.ShouldBeFalse();
        data.JevApiKey.ShouldBe("jev");
        data.MicrophoneDeviceId.ShouldBe("b-mic");
        data.ModelDirectory.ShouldBe(@"D:\models");
        data.PushToTalkKey.ShouldBe(0xA5);
        data.PushToTalkModifiers.ShouldBe(2);
        data.HasOnboarded.ShouldBeTrue();
        data.IsEnabled.ShouldBeFalse();
        data.SyncEnabled.ShouldBeTrue();
        data.SyncServer.ShouldBe("https://staging.test");

        // And none of them went to the server.
        var sent = _server.Live[(SyncKinds.Settings, SyncKinds.SettingsKey)]!.Value;
        foreach (var field in SyncedSettings.MachineFields) sent.TryGetProperty(field, out _).ShouldBeFalse(field);
        sent.TryGetProperty(nameof(SettingsData.GeminiApiKey), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_pc_takes_the_servers_settings_rather_than_pushing_its_defaults()
    {
        var a = NewPc("A");
        a.Settings.Update(a.Settings.Data with { SendWord = "send", AiCleanup = true });
        a.Dictionary.Add(DictionaryEntry.Term("Xero"));
        await a.SyncAsync();

        // A fresh install, an hour later by its clock: its defaults are "newer" than A's
        // settings, and must still not win.
        var b = NewPc("B", Pc.Start.AddHours(1));
        b.Dictionary.Add(DictionaryEntry.Term("PAYE"));
        await b.SyncAsync();
        await a.SyncAsync();

        b.Settings.Data.SendWord.ShouldBe("send");
        b.Settings.Data.AiCleanup.ShouldBeTrue();
        a.Settings.Data.SendWord.ShouldBe("send");
        // Both dictionaries merge.
        a.Dictionary.Entries.Select(e => e.Write).ShouldBe(["Xero", "PAYE"], ignoreOrder: true);
        b.Dictionary.Entries.Select(e => e.Write).ShouldBe(["Xero", "PAYE"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_new_pc_signing_in_first_does_not_push_its_untouched_defaults()
    {
        var b = NewPc("B", Pc.Start.AddHours(1));
        b.Settings.Update(b.Settings.Data with { MicrophoneDeviceId = "b-mic", HasOnboarded = true });
        await b.SyncAsync();
        _server.Live.ContainsKey((SyncKinds.Settings, SyncKinds.SettingsKey)).ShouldBeFalse();

        var a = NewPc("A");
        a.Settings.Update(a.Settings.Data with { SendWord = "send", CustomInstructions = "British spellings" });
        await a.SyncAsync();
        await b.SyncAsync();

        b.Settings.Data.SendWord.ShouldBe("send");
        b.Settings.Data.CustomInstructions.ShouldBe("British spellings");
        b.Settings.Data.MicrophoneDeviceId.ShouldBe("b-mic");
    }

    [Fact]
    public async Task Own_echoes_are_ignored_even_when_the_server_reorders_the_json()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        a.History.Add(Record("one", 1));
        a.Suggestions.Offer("Sarif", "serif", null, Pc.Start);
        await a.SyncAsync();
        await b.SyncAsync();

        var events = 0;
        a.History.Changed += (_, _) => events++;
        a.Suggestions.Changed += (_, _) => events++;
        a.Settings.Changed += (_, _) => events++;
        b.History.Changed += (_, _) => events++;
        b.Suggestions.Changed += (_, _) => events++;

        for (var i = 0; i < 3; i++)
        {
            var again = await a.SyncAsync();
            again.Pushed.ShouldBe(0);
            again.Applied.ShouldBe(0);
            var other = await b.SyncAsync();
            other.Pushed.ShouldBe(0);
            other.Applied.ShouldBe(0);
        }
        events.ShouldBe(0);
    }

    [Fact]
    public async Task Store_events_raised_by_sync_say_they_are_syncs_own()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        a.Dictionary.Add(DictionaryEntry.Term("Sidgrove"));
        await a.SyncAsync();

        var flags = new List<bool>();
        b.Dictionary.Changed += (_, _) => flags.Add(SyncEngine.IsApplyingOnThisThread);
        await b.SyncAsync();
        b.Dictionary.Add(DictionaryEntry.Term("Xero"));

        flags.ShouldBe([true, false]);
        SyncEngine.IsApplyingOnThisThread.ShouldBeFalse();
    }

    [Fact]
    public async Task A_local_change_is_not_overwritten_by_a_pulled_one()
    {
        var a = NewPc("A");
        var b = NewPc("B", Pc.Start.AddMinutes(1));
        await a.SyncAsync();
        await b.SyncAsync();

        a.Settings.Update(a.Settings.Data with { SendWord = "from-a" });
        await a.SyncAsync();
        // B changed it too, before hearing about A's change, and syncs later.
        b.Settings.Update(b.Settings.Data with { SendWord = "from-b" });
        var result = await b.SyncAsync(afterSeconds: 30);

        b.Settings.Data.SendWord.ShouldBe("from-b");
        result.Pushed.ShouldBe(1);
        await a.SyncAsync();
        a.Settings.Data.SendWord.ShouldBe("from-b");
    }

    [Fact]
    public async Task A_push_that_loses_takes_the_servers_copy()
    {
        var a = NewPc("A", Pc.Start.AddHours(1));
        // B's clock is behind, so its later edit loses on the server.
        var b = NewPc("B");
        await a.SyncAsync();
        await b.SyncAsync();

        a.Settings.Update(a.Settings.Data with { SendWord = "from-a" });
        await a.SyncAsync();
        b.Settings.Update(b.Settings.Data with { SendWord = "from-b" });
        await b.SyncAsync();

        b.Settings.Data.SendWord.ShouldBe("from-a");
        // Settled: nothing more to push either way.
        (await b.SyncAsync()).Pushed.ShouldBe(0);
        (await a.SyncAsync()).Applied.ShouldBe(0);
        a.Settings.Data.SendWord.ShouldBe("from-a");
    }

    [Fact]
    public async Task Paging_carries_on_while_the_server_says_there_is_more()
    {
        _server.PageSize = 2;
        var a = NewPc("A");
        for (var i = 0; i < 5; i++) a.History.Add(Record($"dictation {i}", i));
        await a.SyncAsync();

        var b = NewPc("B");
        var before = _server.Requests.Count;
        var pulled = await b.SyncAsync();

        // Five records two at a time.
        (_server.Requests.Count - before).ShouldBeGreaterThanOrEqualTo(3);
        pulled.Pulled.ShouldBe(5);
        b.History.Records.Select(r => r.Text).ShouldBe(["dictation 4", "dictation 3", "dictation 2", "dictation 1", "dictation 0"]);
        // The cursor only ever comes from the reply.
        _server.Requests.Skip(before).Select(r => r.Cursor).ShouldBe(_server.Requests.Skip(before).Select(r => r.Cursor).Order());
    }

    [Fact]
    public async Task Pushes_are_split_into_calls_of_at_most_five_hundred()
    {
        var a = NewPc("A");
        a.History.Merge(Enumerable.Range(0, 1203).Select(i => Record($"dictation {i}", i)), []);
        await a.SyncAsync();

        _server.Requests.Max(r => r.Changes.Count).ShouldBeLessThanOrEqualTo(SyncEngine.MaxChangesPerCall);
        _server.Live.Count(r => r.Key.Kind == SyncKinds.History).ShouldBe(1203);
    }

    [Fact]
    public async Task A_corrected_recording_is_uploaded_once_and_downloaded_on_the_other_pc()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        var kept = Record("the one that needed fixing", 5);
        var plain = Record("the one that did not", 6);
        a.History.Add(kept);
        a.History.Add(plain);
        Directory.CreateDirectory(a.Archive.KeptFolder);
        byte[] wav = [82, 73, 70, 70, 1, 2, 3, 4];
        File.WriteAllBytes(a.Archive.KeptPathFor(kept.At), wav);

        var first = await a.SyncAsync();
        first.Uploaded.ShouldBe(1);
        _server.Files.Keys.ShouldBe([kept.Id.ToString("D")]);
        _server.Live.Keys.ShouldContain((SyncKinds.Recording, kept.Id.ToString("D")));
        (await a.SyncAsync()).Uploaded.ShouldBe(0);

        var arrived = await b.SyncAsync();
        arrived.Downloaded.ShouldBe(1);
        File.ReadAllBytes(b.Archive.KeptPathFor(kept.At)).ShouldBe(wav);
        File.Exists(b.Archive.KeptPathFor(plain.At)).ShouldBeFalse();

        // B now has it, so B neither fetches nor announces it again.
        var settled = await b.SyncAsync();
        settled.Downloaded.ShouldBe(0);
        settled.Pushed.ShouldBe(0);
        _server.Uploads.ShouldBe(1);
    }

    [Fact]
    public async Task A_recording_that_could_not_be_fetched_is_tried_again_next_run()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        var kept = Record("fixed", 5);
        a.History.Add(kept);
        Directory.CreateDirectory(a.Archive.KeptFolder);
        File.WriteAllBytes(a.Archive.KeptPathFor(kept.At), [1, 2, 3]);
        await a.SyncAsync();

        var file = _server.Files[kept.Id.ToString("D")];
        _server.Files.Clear();
        (await b.SyncAsync()).Downloaded.ShouldBe(0);
        File.Exists(b.Archive.KeptPathFor(kept.At)).ShouldBeFalse();

        // Not on the server is final; a missing file is not asked for again.
        _server.Files[kept.Id.ToString("D")] = file;
        (await b.SyncAsync()).Downloaded.ShouldBe(0);
    }

    [Fact]
    public async Task A_network_failure_mid_download_leaves_it_pending()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        var kept = Record("fixed", 5);
        a.History.Add(kept);
        Directory.CreateDirectory(a.Archive.KeptFolder);
        File.WriteAllBytes(a.Archive.KeptPathFor(kept.At), [1, 2, 3]);
        await a.SyncAsync();

        var flaky = new FlakyDownloads(_server);
        var bFlaky = new SyncEngine(flaky, b.StatePath, "B", b.Dictionary, b.Suggestions, b.History, b.Settings, b.Archive, b.Clock);
        await Should.ThrowAsync<HttpRequestException>(() => bFlaky.RunAsync(CancellationToken.None));
        File.Exists(b.Archive.KeptPathFor(kept.At)).ShouldBeFalse();

        (await b.SyncAsync()).Downloaded.ShouldBe(1);
        File.ReadAllBytes(b.Archive.KeptPathFor(kept.At)).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task A_history_record_deleted_on_one_pc_goes_from_the_other()
    {
        var a = NewPc("A");
        var b = NewPc("B");
        var one = Record("one", 1);
        var two = Record("two", 2);
        a.History.Add(one);
        a.History.Add(two);
        await a.SyncAsync();
        await b.SyncAsync();
        b.History.Records.Count.ShouldBe(2);

        b.History.Remove(one.Id);
        await b.SyncAsync();
        await a.SyncAsync();

        a.History.Records.Single().Id.ShouldBe(two.Id);
    }

    [Fact]
    public async Task A_row_that_cannot_be_read_is_ignored()
    {
        var b = NewPc("B");
        using var bad = JsonDocument.Parse("""{"line":""}""");
        await _server.SyncAsync(new SyncRequest
        {
            Device = "elsewhere",
            Changes =
            [
                new SyncChange { Kind = SyncKinds.Dictionary, Key = "Term|Ghost", Data = bad.RootElement.Clone(), UpdatedAt = Pc.Start },
                new SyncChange { Kind = "unknown-kind", Key = "x", Data = bad.RootElement.Clone(), UpdatedAt = Pc.Start },
            ],
        }, CancellationToken.None);

        var result = await b.SyncAsync();
        result.Applied.ShouldBe(0);
        b.Dictionary.Entries.ShouldBeEmpty();
    }

    private sealed class FlakyDownloads(InMemorySyncServer inner) : ISyncServer
    {
        public Task<SyncReply> SyncAsync(SyncRequest request, CancellationToken cancellationToken) => inner.SyncAsync(request, cancellationToken);
        public Task<Uri?> RecordingUrlAsync(string key, RecordingTransfer transfer, CancellationToken cancellationToken) => inner.RecordingUrlAsync(key, transfer, cancellationToken);
        public Task UploadAsync(Uri url, byte[] wav, CancellationToken cancellationToken) => inner.UploadAsync(url, wav, cancellationToken);
        public Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken) => throw new HttpRequestException("connection reset");
        public Task RevokeAsync(CancellationToken cancellationToken) => inner.RevokeAsync(cancellationToken);
    }
}

/// <summary>The background service around the engine: never throws, backs off, signs out on a 401.</summary>
public sealed class SyncServiceTests : IDisposable
{
    private readonly InMemorySyncServer _server = new();
    private readonly Pc _pc;
    private readonly SyncAccount _account;

    public SyncServiceTests()
    {
        Log.Path = Path.Combine(Path.GetTempPath(), $"murmur-tests-{Environment.ProcessId}.log");
        _pc = new Pc(_server, "A");
        _account = new SyncAccount(Path.Combine(_pc.Folder, "sync-token.bin"), new ReversingSecrets());
        _account.Save("sg_acapella_test", "dave@sidgrove.com");
        _pc.Settings.Update(_pc.Settings.Data with { SyncEnabled = true });
    }

    public void Dispose() => _pc.Dispose();

    private SyncService NewService(Func<CancellationToken, Task<SignInResult>>? signIn = null) =>
        new(_pc.Engine, _server, _account, _pc.Settings, signIn)
        {
            Debounce = TimeSpan.FromMilliseconds(50),
            Interval = TimeSpan.FromHours(1),
            FirstBackoff = TimeSpan.FromMilliseconds(50),
            MaxBackoff = TimeSpan.FromMilliseconds(200),
        };

    [Fact]
    public async Task Never_throws_when_the_server_cannot_be_reached()
    {
        _server.Fail = () => new HttpRequestException("No such host is known.");
        await using var service = NewService();

        (await service.SyncNowAsync()).ShouldBeFalse();
        service.Status.Problem.ShouldNotBeNull().ShouldContain("Couldn't reach");
        service.Status.IsSignedIn.ShouldBeTrue();

        _server.Fail = () => new InvalidOperationException("something unexpected");
        (await service.SyncNowAsync()).ShouldBeFalse();

        _server.Fail = null;
        (await service.SyncNowAsync()).ShouldBeTrue();
        service.Status.Problem.ShouldBeNull();
        service.Status.LastSyncedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_401_drops_the_token_and_asks_to_sign_in_again()
    {
        _server.Fail = () => new SyncUnauthorizedException();
        await using var service = NewService();

        (await service.SyncNowAsync()).ShouldBeFalse();

        _account.IsSignedIn.ShouldBeFalse();
        File.Exists(Path.Combine(_pc.Folder, "sync-token.bin")).ShouldBeFalse();
        service.Status.IsSignedIn.ShouldBeFalse();
        service.Status.Problem.ShouldNotBeNull().ShouldStartWith("Sign in again");
    }

    [Fact]
    public async Task Runs_at_start_up_and_again_shortly_after_a_change()
    {
        await using var service = NewService();
        service.Start();
        (await Wait.UntilAsync(() => _server.Requests.Count >= 1 && !service.Status.IsRunning)).ShouldBeTrue();

        _pc.Dictionary.Add(DictionaryEntry.Term("Sidgrove"));
        service.Notify();

        (await Wait.UntilAsync(() => _server.Live.ContainsKey((SyncKinds.Dictionary, "Term|Sidgrove")))).ShouldBeTrue();
    }

    [Fact]
    public async Task Backs_off_and_recovers_after_failures()
    {
        _server.Fail = () => new HttpRequestException("offline");
        await using var service = NewService();
        service.Start();
        (await Wait.UntilAsync(() => service.Status.Problem is not null)).ShouldBeTrue();

        _server.Fail = null;
        (await Wait.UntilAsync(() => service.Status.Problem is null && service.Status.LastSyncedAt is not null)).ShouldBeTrue();
    }

    [Fact]
    public async Task Does_nothing_while_signed_out_or_switched_off()
    {
        _pc.Settings.Update(_pc.Settings.Data with { SyncEnabled = false });
        await using var service = NewService();

        (await service.SyncNowAsync()).ShouldBeFalse();
        _server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Signing_in_keeps_the_token_switches_sync_on_and_syncs()
    {
        _account.Clear();
        _pc.Settings.Update(_pc.Settings.Data with { SyncEnabled = false });
        await using var service = NewService(_ => Task.FromResult(new SignInResult("sg_acapella_new", "dave@sidgrove.com", null)));

        var result = await service.SignInAsync(CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        _pc.Settings.Data.SyncEnabled.ShouldBeTrue();
        new SyncAccount(Path.Combine(_pc.Folder, "sync-token.bin"), new ReversingSecrets()).Token.ShouldBe("sg_acapella_new");
        (await Wait.UntilAsync(() => _server.Requests.Count > 0)).ShouldBeTrue();
        service.Status.Email.ShouldBe("dave@sidgrove.com");
    }

    [Fact]
    public async Task A_failed_sign_in_says_why_and_stays_signed_out()
    {
        _account.Clear();
        await using var service = NewService(_ => Task.FromResult(new SignInResult(null, null, "Sign-in timed out. Try again when you're ready.")));

        var result = await service.SignInAsync(CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        service.Status.IsSignedIn.ShouldBeFalse();
        service.Status.Problem.ShouldBe("Sign-in timed out. Try again when you're ready.");
    }

    [Fact]
    public async Task Signing_out_revokes_and_forgets_everything()
    {
        await using var service = NewService();
        await service.SyncNowAsync();
        File.Exists(_pc.StatePath).ShouldBeTrue();

        await service.SignOutAsync();

        _server.Revoked.ShouldBeTrue();
        _account.IsSignedIn.ShouldBeFalse();
        _pc.Settings.Data.SyncEnabled.ShouldBeFalse();
        File.Exists(_pc.StatePath).ShouldBeFalse();
        service.Status.LastSyncedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Signing_out_works_offline()
    {
        await using var service = NewService();
        _server.Fail = () => new HttpRequestException("offline");

        await service.SignOutAsync();

        _account.IsSignedIn.ShouldBeFalse();
    }

    [Fact]
    public void The_token_file_is_not_readable_as_plain_text()
    {
        var bytes = File.ReadAllBytes(Path.Combine(_pc.Folder, "sync-token.bin"));
        System.Text.Encoding.UTF8.GetString(bytes).ShouldNotContain("sg_acapella_test");
        new SyncAccount(Path.Combine(_pc.Folder, "sync-token.bin"), new ReversingSecrets()).Email.ShouldBe("dave@sidgrove.com");
        // Without the secret store (off Windows) nothing can be read or kept.
        new SyncAccount(Path.Combine(_pc.Folder, "sync-token.bin"), null).IsSignedIn.ShouldBeFalse();
    }

    /// <summary>Stands in for DPAPI: reverses the bytes, which is enough to prove they are not stored plain.</summary>
    private sealed class ReversingSecrets : ISecretStore
    {
        public byte[] Protect(byte[] plain) => [.. plain.Reverse()];
        public byte[]? Unprotect(byte[] cipher) => [.. cipher.Reverse()];
    }
}

/// <summary>The store methods sync applies changes through.</summary>
public sealed class SyncStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"acapella-sync-store-{Guid.NewGuid():N}");

    public SyncStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void History_merge_keeps_newest_first_with_one_event_and_one_rewrite()
    {
        var store = new TranscriptStore(Path.Combine(_folder, "t.jsonl"));
        var old = new TranscriptRecord { At = Pc.Start, Text = "old" };
        var middle = new TranscriptRecord { At = Pc.Start.AddMinutes(1), Text = "middle" };
        store.Add(old);
        store.Add(middle);
        var events = 0;
        store.Changed += (_, _) => events++;

        var newest = new TranscriptRecord { At = Pc.Start.AddMinutes(2), Text = "newest" };
        var earliest = new TranscriptRecord { At = Pc.Start.AddMinutes(-1), Text = "earliest" };
        store.Merge([newest, earliest, middle with { Text = "middle, edited" }], [old.Id]);

        events.ShouldBe(1);
        store.Records.Select(r => r.Text).ShouldBe(["newest", "middle, edited", "earliest"]);
        new TranscriptStore(Path.Combine(_folder, "t.jsonl")).Records.Select(r => r.Text).ShouldBe(["newest", "middle, edited", "earliest"]);
    }

    [Fact]
    public void Suggestion_merge_replaces_by_id_adds_and_removes()
    {
        var store = new SuggestionStore(Path.Combine(_folder, "s.json"));
        var keep = store.Offer("Gev", "Jev", null, Pc.Start);
        var drop = store.Offer("Sarif", "serif", null, Pc.Start);

        store.Merge([keep with { Count = 5 }, new DictionarySuggestion { Hear = "Hoxon", Write = "Hoxton" }], [drop.Id]);

        store.All.Select(s => (s.Hear, s.Count)).ShouldBe([("Gev", 5), ("Hoxon", 1)]);
        new SuggestionStore(Path.Combine(_folder, "s.json")).All.Count.ShouldBe(2);
    }

    [Fact]
    public void Dictionary_edit_saves_once()
    {
        var file = new DictionaryFile(Path.Combine(_folder, "d.txt"));
        file.Add(DictionaryEntry.Term("Xero"));
        var events = 0;
        file.Changed += (_, _) => events++;

        file.Edit(entries => [.. entries, DictionaryEntry.Term("PAYE"), DictionaryEntry.Correction("get pole", "git pull")]);

        events.ShouldBe(1);
        new DictionaryFile(Path.Combine(_folder, "d.txt")).Entries.Select(e => e.ToFileLine()).ShouldBe(["Xero", "PAYE", "get pole -> git pull"]);
    }

    [Fact]
    public void Dictionary_keys_are_as_the_contract_says()
    {
        SyncKinds.DictionaryKey(DictionaryEntry.Term("Sidgrove")).ShouldBe("Term|Sidgrove");
        SyncKinds.DictionaryKey(DictionaryEntry.Correction("Cork Tax", "Corp Tax")).ShouldBe("Correction|cork tax|Corp Tax");
        SyncKinds.DictionaryKey(DictionaryEntry.Correction("Cork Tax", "Corp Tax") with { IsEnabled = false }).ShouldBe("Correction|cork tax|Corp Tax");
    }

    [Fact]
    public void The_wire_stamps_times_in_utc_with_milliseconds()
    {
        var change = new SyncChange { Kind = "history", Key = "k", UpdatedAt = new DateTimeOffset(2026, 10, 2, 14, 17, 45, 712, TimeSpan.FromHours(1)) };
        var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        JsonSerializer.Serialize(change, options).ShouldContain("\"updatedAt\":\"2026-10-02T13:17:45.712Z\"");
        JsonSerializer.Deserialize<SyncChange>("""{"kind":"history","key":"k","updatedAt":"2026-10-02T13:17:45.712Z"}""", options)!
            .UpdatedAt.ShouldBe(new DateTimeOffset(2026, 10, 2, 13, 17, 45, 712, TimeSpan.Zero));
    }
}
