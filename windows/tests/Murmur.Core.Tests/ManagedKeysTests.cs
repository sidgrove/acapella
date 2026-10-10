using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Core.Sync;
using Murmur.Speech;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>Stands in for DPAPI: reverses the bytes, which is enough to prove they are not stored plain.</summary>
internal sealed class ReversingKeySecrets : ISecretStore
{
    public byte[] Protect(byte[] plain) => [.. plain.Reverse()];
    public byte[]? Unprotect(byte[] cipher) => [.. cipher.Reverse()];
}

/// <summary>The keys from Sidgrove Intelligence on disk (docs/sync.md, "Keys"), and which key a call is given.</summary>
public sealed class ManagedKeysTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"acapella-keys-{Guid.NewGuid():N}");
    private readonly string _path;

    public ManagedKeysTests()
    {
        Log.Path = Path.Combine(Path.GetTempPath(), $"murmur-tests-{Environment.ProcessId}.log");
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "managed-keys.bin");
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Keys_come_back_after_a_restart()
    {
        var keys = new ManagedKeys(_path, new ReversingKeySecrets());
        var events = 0;
        keys.Changed += (_, _) => events++;

        keys.Save("gemini-key", "eleven-key", null, " gateway-key ");

        events.ShouldBe(1);
        var reopened = new ManagedKeys(_path, new ReversingKeySecrets());
        reopened.Gemini.ShouldBe("gemini-key");
        reopened.ElevenLabs.ShouldBe("eleven-key");
        reopened.Anthropic.ShouldBeNull();
        reopened.AiGateway.ShouldBe("gateway-key");
        reopened.Count.ShouldBe(3);
    }

    [Fact]
    public void A_new_set_replaces_all_four()
    {
        var keys = new ManagedKeys(_path, new ReversingKeySecrets());
        keys.Save("gemini-key", "eleven-key", "anthropic-key", "gateway-key");

        keys.Save(new ManagedKeySet("gemini-key-2", null, "anthropic-key", null));

        var reopened = new ManagedKeys(_path, new ReversingKeySecrets());
        reopened.Gemini.ShouldBe("gemini-key-2");
        reopened.ElevenLabs.ShouldBeNull();
        reopened.Anthropic.ShouldBe("anthropic-key");
        reopened.AiGateway.ShouldBeNull();
    }

    [Fact]
    public void The_file_does_not_hold_a_key_in_the_clear()
    {
        new ManagedKeys(_path, new ReversingKeySecrets()).Save("gemini-key", "eleven-key", "anthropic-key", "gateway-key");

        var written = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(_path));
        written.ShouldNotContain("gemini-key");
        written.ShouldNotContain("eleven-key");
        written.ShouldNotContain("anthropic-key");
        written.ShouldNotContain("gateway-key");
        // No temporary copy left beside it either.
        Directory.GetFiles(_folder).Single().ShouldBe(_path);
    }

    [Fact]
    public void A_file_this_user_cannot_decrypt_loads_empty()
    {
        new ManagedKeys(_path, new ReversingKeySecrets()).Save("gemini-key", "eleven-key", "anthropic-key", "gateway-key");

        var elsewhere = new ManagedKeys(_path, new RefusingSecrets());

        elsewhere.Count.ShouldBe(0);
        elsewhere.Gemini.ShouldBeNull();
        elsewhere.AiGateway.ShouldBeNull();
    }

    [Fact]
    public void A_file_that_is_not_keys_at_all_loads_empty()
    {
        File.WriteAllText(_path, "not what was written");

        new ManagedKeys(_path, new ReversingKeySecrets()).Count.ShouldBe(0);
    }

    [Fact]
    public void Clear_deletes_the_file()
    {
        var keys = new ManagedKeys(_path, new ReversingKeySecrets());
        keys.Save("gemini-key", null, null, null);
        File.Exists(_path).ShouldBeTrue();
        var events = 0;
        keys.Changed += (_, _) => events++;

        keys.Clear();

        File.Exists(_path).ShouldBeFalse();
        keys.Gemini.ShouldBeNull();
        events.ShouldBe(1);
        new ManagedKeys(_path, new ReversingKeySecrets()).Count.ShouldBe(0);
    }

    [Fact]
    public void A_set_with_no_keys_in_it_leaves_no_file()
    {
        var keys = new ManagedKeys(_path, new ReversingKeySecrets());
        keys.Save("gemini-key", null, null, null);

        keys.Save(null, " ", null, string.Empty);

        keys.Count.ShouldBe(0);
        File.Exists(_path).ShouldBeFalse();
    }

    [Fact]
    public void Without_a_secret_store_keys_last_in_memory_only()
    {
        // Someone else's file at the path (the user's own, in a preview) is neither read nor removed.
        new ManagedKeys(_path, new ReversingKeySecrets()).Save("someone-elses-key", null, null, null);
        var before = File.ReadAllBytes(_path);
        var keys = new ManagedKeys(_path, secrets: null);
        keys.Count.ShouldBe(0);

        keys.Save("gemini-key", "eleven-key", "anthropic-key", "gateway-key");
        keys.Gemini.ShouldBe("gemini-key");
        keys.AiGateway.ShouldBe("gateway-key");
        File.ReadAllBytes(_path).ShouldBe(before);

        keys.Clear();
        keys.Count.ShouldBe(0);
        File.ReadAllBytes(_path).ShouldBe(before);
    }

    [Fact]
    public void The_set_never_prints_its_keys()
    {
        var set = new ManagedKeySet("gemini-key", null, "anthropic-key", null);

        set.ToString().ShouldNotContain("gemini-key");
        $"{set}".ShouldNotContain("anthropic-key");
        set.Count.ShouldBe(2);
    }

    [Fact]
    public void Typed_beats_managed_beats_the_environment()
    {
        var settings = new AppSettings(Path.Combine(_folder, "settings.json"));
        var managed = new ManagedKeys(_path, new ReversingKeySecrets());
        var keys = new ApiKeys(settings, managed);

        // Nothing typed and nothing managed: null, which every client reads as "my environment variable".
        keys.Gemini().ShouldBeNull();
        keys.ElevenLabs().ShouldBeNull();
        keys.Anthropic().ShouldBeNull();
        keys.Jev().ShouldBeNull();
        GeminiCleaner.ResolveKey(keys.Gemini()).ShouldBe(GeminiCleaner.ResolveKey(null));
        ElevenLabsTranscriber.ResolveKey(keys.ElevenLabs()).ShouldBe(ElevenLabsTranscriber.ResolveKey(null));
        ClaudeCleaner.ResolveKey(keys.Anthropic()).ShouldBe(ClaudeCleaner.ResolveKey(null));
        JevClient.ResolveKey(keys.Jev()).ShouldBe(JevClient.ResolveKey(null));

        // Managed keys arrive: used from the next call, whatever the environment holds.
        managed.Save("gemini-managed", "eleven-managed", "anthropic-managed", "gateway-managed");
        GeminiCleaner.ResolveKey(keys.Gemini()).ShouldBe("gemini-managed");
        ElevenLabsTranscriber.ResolveKey(keys.ElevenLabs()).ShouldBe("eleven-managed");
        ClaudeCleaner.ResolveKey(keys.Anthropic()).ShouldBe("anthropic-managed");
        JevClient.ResolveKey(keys.Jev()).ShouldBe("gateway-managed");

        // A key typed into Settings wins over the managed one.
        settings.Update(settings.Data with { GeminiApiKey = "gemini-typed", ElevenLabsApiKey = "eleven-typed", AnthropicApiKey = "anthropic-typed", JevApiKey = "gateway-typed" });
        GeminiCleaner.ResolveKey(keys.Gemini()).ShouldBe("gemini-typed");
        ElevenLabsTranscriber.ResolveKey(keys.ElevenLabs()).ShouldBe("eleven-typed");
        ClaudeCleaner.ResolveKey(keys.Anthropic()).ShouldBe("anthropic-typed");
        JevClient.ResolveKey(keys.Jev()).ShouldBe("gateway-typed");

        // One by one: a blank field falls back to the managed key for that provider alone.
        settings.Update(settings.Data with { GeminiApiKey = "  ", JevApiKey = null });
        keys.Gemini().ShouldBe("gemini-managed");
        keys.ElevenLabs().ShouldBe("eleven-typed");
        keys.Jev().ShouldBe("gateway-managed");

        // Signed out: back to typed, else the environment.
        managed.Clear();
        keys.Gemini().ShouldBeNull();
        keys.Anthropic().ShouldBe("anthropic-typed");
    }

    [Fact]
    public void Managed_keys_never_reach_the_settings_file()
    {
        var settingsPath = Path.Combine(_folder, "settings.json");
        var settings = new AppSettings(settingsPath, new ReversingKeySecrets());
        var managed = new ManagedKeys(_path, new ReversingKeySecrets());
        var keys = new ApiKeys(settings, managed);

        managed.Save("gemini-managed", "eleven-managed", "anthropic-managed", "gateway-managed");
        keys.Gemini().ShouldBe("gemini-managed");
        settings.Update(settings.Data with { SendWord = "send" });

        settings.Data.GeminiApiKey.ShouldBeNull();
        settings.Data.ElevenLabsApiKey.ShouldBeNull();
        settings.Data.AnthropicApiKey.ShouldBeNull();
        settings.Data.JevApiKey.ShouldBeNull();
        new AppSettings(settingsPath, new ReversingKeySecrets()).Data.GeminiApiKey.ShouldBeNull();
    }

    private sealed class RefusingSecrets : ISecretStore
    {
        public byte[] Protect(byte[] plain) => plain;
        public byte[]? Unprotect(byte[] cipher) => null;
    }
}

/// <summary>When the keys are fetched: at sign-in, at start, every 12 hours, and never in a sync run's way.</summary>
public sealed class ManagedKeyRefreshTests : IDisposable
{
    private static readonly ManagedKeySet Held = new("gemini-key", "eleven-key", null, "gateway-key");

    private readonly InMemorySyncServer _server = new() { Keys = Held };
    private readonly Pc _pc;
    private readonly SyncAccount _account;
    private readonly ManagedKeys _keys;
    private readonly string _keysPath;

    public ManagedKeyRefreshTests()
    {
        Log.Path = Path.Combine(Path.GetTempPath(), $"murmur-tests-{Environment.ProcessId}.log");
        _pc = new Pc(_server, "A");
        _account = new SyncAccount(Path.Combine(_pc.Folder, "sync-token.bin"), new ReversingKeySecrets());
        _account.Save("sg_acapella_test", "dave@sidgrove.com");
        _keysPath = Path.Combine(_pc.Folder, "managed-keys.bin");
        _keys = new ManagedKeys(_keysPath, new ReversingKeySecrets());
        _pc.Settings.Update(_pc.Settings.Data with { SyncEnabled = true });
    }

    public void Dispose() => _pc.Dispose();

    private SyncService NewService(
        Func<CancellationToken, Task<SignInResult>>? signIn = null,
        IManagedKeyServer? keyServer = null,
        TimeSpan? refresh = null,
        TimeSpan? retry = null)
        => new(_pc.Engine, _server, _account, _pc.Settings, signIn)
        {
            Debounce = TimeSpan.FromMilliseconds(50),
            Interval = TimeSpan.FromHours(1),
            FirstBackoff = TimeSpan.FromMilliseconds(50),
            MaxBackoff = TimeSpan.FromMilliseconds(200),
            Keys = _keys,
            KeyServer = keyServer ?? _server,
            // The shipped rhythm (see the test below) unless the test is about what happens after it.
            KeyRefreshInterval = refresh ?? TimeSpan.FromHours(12),
            KeyRetryInterval = retry ?? TimeSpan.FromMinutes(5),
        };

    [Fact]
    public async Task The_shipped_rhythm_is_12_hours_with_a_retry_after_5_minutes()
    {
        await using var service = new SyncService(_pc.Engine, _server, _account, _pc.Settings, null);

        service.KeyRefreshInterval.ShouldBe(TimeSpan.FromHours(12));
        service.KeyRetryInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Signing_in_fetches_the_keys_and_keeps_them_encrypted()
    {
        _account.Clear();
        _pc.Settings.Update(_pc.Settings.Data with { SyncEnabled = false });
        _server.Keys = new ManagedKeySet("gemini-key-not-for-the-log", "eleven-key", null, "gateway-key");
        await using var service = NewService(_ => Task.FromResult(new SignInResult("sg_acapella_new", "dave@sidgrove.com", null)));

        (await service.SignInAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();

        (await Wait.UntilAsync(() => _keys.Count == 3)).ShouldBeTrue();
        _keys.Gemini.ShouldBe("gemini-key-not-for-the-log");
        _keys.ElevenLabs.ShouldBe("eleven-key");
        _keys.Anthropic.ShouldBeNull();
        _keys.AiGateway.ShouldBe("gateway-key");
        new ManagedKeys(_keysPath, new ReversingKeySecrets()).Gemini.ShouldBe("gemini-key-not-for-the-log");
        File.ReadAllText(Path.Combine(_pc.Folder, "settings.json")).ShouldNotContain("gemini-key-not-for-the-log");
        ReadShared(Log.Path).ShouldNotContain("gemini-key-not-for-the-log");
    }

    [Fact]
    public async Task Signing_in_again_asks_again_even_within_the_12_hours()
    {
        await using var service = NewService(_ => Task.FromResult(new SignInResult("sg_acapella_new", "dave@sidgrove.com", null)));
        await service.SyncNowAsync();
        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key")).ShouldBeTrue();

        _server.Keys = Held with { Gemini = "gemini-key-2" };
        await service.SignInAsync(CancellationToken.None);

        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key-2")).ShouldBeTrue();
    }

    [Fact]
    public async Task Starting_already_signed_in_fetches_the_keys()
    {
        await using var service = NewService();

        service.Start();

        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key")).ShouldBeTrue();
        _keys.AiGateway.ShouldBe("gateway-key");
    }

    [Fact]
    public async Task Signed_in_is_enough_even_with_sync_switched_off()
    {
        _pc.Settings.Update(_pc.Settings.Data with { SyncEnabled = false });
        await using var service = NewService();

        (await service.SyncNowAsync()).ShouldBeFalse();

        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key")).ShouldBeTrue();
        _server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_asked_for_while_signed_out()
    {
        _account.Clear();
        var service = NewService();
        service.Start();
        await service.SyncNowAsync();
        await service.DisposeAsync();

        _server.KeyFetches.ShouldBe(0);
        _keys.Count.ShouldBe(0);
    }

    [Fact]
    public async Task The_keys_are_asked_for_at_most_once_every_12_hours()
    {
        var service = NewService();
        service.Start();
        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key")).ShouldBeTrue();
        for (var run = 0; run < 5; run++) (await service.SyncNowAsync()).ShouldBeTrue();
        await service.DisposeAsync();

        _server.KeyFetches.ShouldBe(1);
    }

    [Fact]
    public async Task Once_the_interval_has_passed_the_next_run_asks_again()
    {
        await using var service = NewService(refresh: TimeSpan.Zero);
        await service.SyncNowAsync();
        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key")).ShouldBeTrue();

        // Changed in Sidgrove Intelligence: here by the next refresh, with no restart.
        _server.Keys = new ManagedKeySet("gemini-key-2", null, "anthropic-key", "gateway-key");
        await service.SyncNowAsync();

        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key-2")).ShouldBeTrue();
        _keys.ElevenLabs.ShouldBeNull();
        _keys.Anthropic.ShouldBe("anthropic-key");
    }

    [Fact]
    public async Task Signing_out_clears_the_keys()
    {
        await using var service = NewService();
        await service.SyncNowAsync();
        (await Wait.UntilAsync(() => File.Exists(_keysPath))).ShouldBeTrue();

        await service.SignOutAsync();

        _keys.Count.ShouldBe(0);
        File.Exists(_keysPath).ShouldBeFalse();
    }

    [Fact]
    public async Task A_refused_sync_drops_the_keys_with_the_token()
    {
        _keys.Save(Held);
        _server.FailKeys = () => new HttpRequestException("offline");
        _server.Fail = () => new SyncUnauthorizedException();
        await using var service = NewService();

        (await service.SyncNowAsync()).ShouldBeFalse();

        _account.IsSignedIn.ShouldBeFalse();
        _keys.Count.ShouldBe(0);
        File.Exists(_keysPath).ShouldBeFalse();
    }

    [Fact]
    public async Task A_401_for_the_keys_means_signed_out()
    {
        _keys.Save(Held);
        _pc.Settings.Update(_pc.Settings.Data with { SyncEnabled = false });
        _server.FailKeys = () => new SyncUnauthorizedException();
        await using var service = NewService();

        await service.SyncNowAsync();

        (await Wait.UntilAsync(() => !_account.IsSignedIn)).ShouldBeTrue();
        (await Wait.UntilAsync(() => service.Status.Problem is not null)).ShouldBeTrue();
        service.Status.Problem.ShouldNotBeNull().ShouldStartWith("Sign in again");
        _keys.Count.ShouldBe(0);
        File.Exists(_keysPath).ShouldBeFalse();
    }

    [Fact]
    public async Task An_older_server_without_the_route_is_no_problem()
    {
        _keys.Save("gemini-key-from-before", null, null, null);
        _server.Keys = null;
        var service = NewService();

        (await service.SyncNowAsync()).ShouldBeTrue();
        (await Wait.UntilAsync(() => _server.KeyFetches == 1)).ShouldBeTrue();
        await service.SyncNowAsync();
        var status = service.Status;
        await service.DisposeAsync();

        status.Problem.ShouldBeNull();
        status.IsSignedIn.ShouldBeTrue();
        _keys.Gemini.ShouldBe("gemini-key-from-before");
        _server.KeyFetches.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_fetch_keeps_the_stored_keys_and_tries_again()
    {
        _keys.Save("gemini-key-from-before", null, null, null);
        _server.FailKeys = () => new HttpRequestException("503 Service Unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable);
        await using var service = NewService(retry: TimeSpan.Zero);

        (await service.SyncNowAsync()).ShouldBeTrue();
        (await Wait.UntilAsync(() => _server.KeyFetches == 1)).ShouldBeTrue();
        service.Status.Problem.ShouldBeNull();
        _keys.Gemini.ShouldBe("gemini-key-from-before");

        _server.FailKeys = null;
        // The retry is due once the failure has been noted, which happens just after the count goes up.
        var deadline = DateTime.UtcNow + Wait.Timeout;
        while (_keys.Gemini != "gemini-key" && DateTime.UtcNow < deadline)
        {
            await service.SyncNowAsync();
            await Task.Delay(5);
        }
        _keys.Gemini.ShouldBe("gemini-key");
    }

    [Fact]
    public async Task A_failed_fetch_is_not_retried_on_every_run()
    {
        _server.FailKeys = () => new HttpRequestException("offline");
        var service = NewService();

        for (var run = 0; run < 5; run++) await service.SyncNowAsync();
        await service.DisposeAsync();

        _server.KeyFetches.ShouldBe(1);
    }

    [Fact]
    public async Task A_slow_fetch_holds_up_neither_a_sync_run_nor_a_sign_in()
    {
        var slow = new SlowKeys(Held);
        await using var service = NewService(_ => Task.FromResult(new SignInResult("sg_acapella_new", "dave@sidgrove.com", null)), slow);

        (await service.SignInAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();
        (await service.SyncNowAsync()).ShouldBeTrue();
        (await service.SyncNowAsync()).ShouldBeTrue();

        (await Wait.UntilAsync(() => slow.Asked >= 1)).ShouldBeTrue();
        _keys.Count.ShouldBe(0);
        slow.Release();
        (await Wait.UntilAsync(() => _keys.Gemini == "gemini-key")).ShouldBeTrue();
    }

    [Fact]
    public async Task Keys_that_arrive_after_signing_out_are_not_kept()
    {
        var slow = new SlowKeys(Held);
        var service = NewService(keyServer: slow);
        await service.SyncNowAsync();
        (await Wait.UntilAsync(() => slow.Asked == 1)).ShouldBeTrue();

        await service.SignOutAsync();
        slow.Release();
        await service.DisposeAsync();

        _keys.Count.ShouldBe(0);
        File.Exists(_keysPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Closing_does_not_wait_on_a_fetch_that_never_answers()
    {
        var slow = new SlowKeys(Held);
        var service = NewService(keyServer: slow);
        await service.SyncNowAsync();
        (await Wait.UntilAsync(() => slow.Asked == 1)).ShouldBeTrue();

        await service.DisposeAsync().AsTask().WaitAsync(Wait.Timeout);

        _keys.Count.ShouldBe(0);
    }

    private static string ReadShared(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A key server that answers only when told to, or when the caller gives up.</summary>
    private sealed class SlowKeys(ManagedKeySet answer) : IManagedKeyServer
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public void Release() => _release.TrySetResult();

        public async Task<ManagedKeySet?> KeysAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _asked);
            await _release.Task.WaitAsync(cancellationToken);
            return answer;
        }
    }
}
