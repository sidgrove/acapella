using System.Diagnostics;
using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Core.Sync;

/// <summary>How sync stands, for the Settings card.</summary>
/// <param name="CanSignIn">Whether signing in is possible on this PC at all.</param>
/// <param name="Email">The signed-in account, or null when signed out.</param>
/// <param name="LastSyncedAt">When a run last finished, or null if never.</param>
/// <param name="IsRunning">Whether a run is under way.</param>
/// <param name="Problem">What went wrong last, in words for the card, or null.</param>
public sealed record SyncStatus(bool CanSignIn, string? Email, DateTimeOffset? LastSyncedAt, bool IsRunning, string? Problem)
{
    /// <summary>Whether there is an account.</summary>
    public bool IsSignedIn => Email is not null;
}

/// <summary>
/// Runs <see cref="SyncEngine"/> in the background: once at start-up, a short while after
/// anything changes, and every few minutes, one run at a time.
/// </summary>
/// <remarks>
/// <para>
/// Never throws into the app. A failed run is one line in the log and a retry after a
/// growing pause (<see cref="FirstBackoff"/> doubling up to <see cref="MaxBackoff"/>), so a
/// PC that is offline for a day does not hammer anything. A 401 drops the token and asks
/// for a fresh sign-in.
/// </para>
/// <para>
/// Store events raised by the engine's own writes are ignored
/// (<see cref="SyncEngine.IsApplyingOnThisThread"/>); without that, every pulled change
/// would schedule another run that finds nothing to do.
/// </para>
/// </remarks>
public sealed class SyncService : IAsyncDisposable
{
    private readonly SyncEngine _engine;
    private readonly ISyncServer _server;
    private readonly SyncAccount _account;
    private readonly AppSettings _settings;
    private readonly Func<CancellationToken, Task<SignInResult>>? _signIn;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _timing = new();
    private Task? _loop;
    private bool _disposed;

    private DateTimeOffset? _dueAt;
    private DateTimeOffset? _firstChangeAt;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private int _failures;
    private bool _running;
    private string? _problem;
    private DateTimeOffset? _lastSyncedAt;

    /// <summary>Creates the service. Nothing runs until <see cref="Start"/>.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server, for revoking the token on sign-out.</param>
    /// <param name="account">The device token.</param>
    /// <param name="settings">The settings, for <see cref="SettingsData.SyncEnabled"/>.</param>
    /// <param name="signIn">Runs the browser sign-in; null where it is not available.</param>
    public SyncService(SyncEngine engine, ISyncServer server, SyncAccount account, AppSettings settings, Func<CancellationToken, Task<SignInResult>>? signIn)
    {
        _engine = engine;
        _server = server;
        _account = account;
        _settings = settings;
        _signIn = signIn;
        _lastSyncedAt = engine.LastSyncedAt;
    }

    /// <summary>How long after a change a run starts, so a burst of edits is one run.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How often a run happens with nothing changed here, to pull what other PCs did.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The pause after the first failure.</summary>
    public TimeSpan FirstBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The longest pause between failed attempts.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Whether a run would do anything: signed in and switched on.</summary>
    public bool IsActive => _account.IsSignedIn && _settings.Data.SyncEnabled;

    /// <summary>How sync stands now.</summary>
    public SyncStatus Status => new(_account.CanSignIn && _signIn is not null, _account.Email, _lastSyncedAt, _running, _problem);

    /// <summary>Raised whenever <see cref="Status"/> changes. Any thread.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Starts the background loop, with a run straight away.</summary>
    public void Start()
    {
        if (_loop is not null) return;
        lock (_timing) _dueAt = Now;
        _loop = Task.Run(() => LoopAsync(_stop.Token));
    }

    /// <summary>
    /// Something changed in a store: run after <see cref="Debounce"/>. Ignores the engine's
    /// own writes. Safe from any thread, and cheap enough to call from every store event.
    /// </summary>
    public void Notify()
    {
        if (SyncEngine.IsApplyingOnThisThread || !IsActive) return;
        lock (_timing)
        {
            var now = Now;
            _firstChangeAt ??= now;
            // Debounced, but a steady trickle of edits cannot put a run off for ever.
            var latest = _firstChangeAt.Value + Debounce * 4;
            var due = now + Debounce;
            _dueAt = due < latest ? due : latest;
        }
        Wake();
    }

    /// <summary>Runs now (after any run in progress) and says whether it worked. Never throws.</summary>
    public Task<bool> SyncNowAsync() => _disposed ? Task.FromResult(false) : RunGuardedAsync(_stop.Token);

    /// <summary>
    /// Signs in through the browser, keeps the token, switches sync on and starts a run.
    /// Never throws; a failure comes back as <see cref="SignInResult.Error"/>.
    /// </summary>
    public async Task<SignInResult> SignInAsync(CancellationToken cancellationToken)
    {
        if (_signIn is null || !_account.CanSignIn) return new SignInResult(null, null, "Signing in isn't available on this PC.");

        SignInResult result;
        try
        {
            result = await _signIn(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Error("sync: sign-in failed", e);
            result = new SignInResult(null, null, "Sign-in didn't finish. Try again.");
        }

        if (result is { Succeeded: true, Token: { } token })
        {
            _account.Save(token, result.Email ?? string.Empty);
            _problem = null;
            if (!_settings.Data.SyncEnabled) _settings.Update(_settings.Data with { SyncEnabled = true });
            Log.Info($"sync: signed in as {result.Email}");
            Raise();
            _ = SyncNowAsync();
        }
        else
        {
            _problem = result.Error;
            Log.Warn($"sync: sign-in failed: {result.Error}");
            Raise();
        }
        return result;
    }

    /// <summary>
    /// Revokes the token (best effort), forgets it and the sync state, and switches sync off.
    /// Never throws.
    /// </summary>
    public async Task SignOutAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_account.IsSignedIn)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _server.RevokeAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or SyncUnauthorizedException or IOException)
                {
                    Log.Warn($"sync: token could not be revoked, forgetting it anyway: {e.Message}");
                }
            }
            _account.Clear();
            _engine.Reset();
            _lastSyncedAt = null;
            _problem = null;
            _failures = 0;
            if (_settings.Data.SyncEnabled) _settings.Update(_settings.Data with { SyncEnabled = false });
            Log.Info("sync: signed out");
        }
        finally
        {
            _gate.Release();
        }
        Raise();
    }

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already awake.
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;
            lock (_timing)
            {
                DateTimeOffset due;
                if (_failures > 0)
                {
                    // After a failure the retry is due after the back-off, and no sooner:
                    // edits made while offline wait for it rather than each trying again.
                    due = _lastAttempt + Backoff(_failures);
                }
                else
                {
                    due = _lastAttempt == DateTimeOffset.MinValue ? Now : _lastAttempt + Interval;
                    if (_dueAt is { } asked && asked < due) due = asked;
                }
                wait = due - Now;
            }

            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await _wake.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            lock (_timing)
            {
                _dueAt = null;
                _firstChangeAt = null;
            }
            await RunGuardedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan Backoff(int failures)
    {
        var pause = FirstBackoff * Math.Pow(2, Math.Min(failures - 1, 16));
        return pause < MaxBackoff ? pause : MaxBackoff;
    }

    private async Task<bool> RunGuardedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            lock (_timing) _lastAttempt = Now;
            if (!IsActive) return false;

            _running = true;
            Raise();
            var clock = Stopwatch.StartNew();
            var result = await _engine.RunAsync(cancellationToken).ConfigureAwait(false);
            if (result.DidAnything || _failures > 0 || _lastSyncedAt is null)
            {
                Log.Info($"sync: pushed {result.Pushed}, pulled {result.Pulled}, applied {result.Applied}, uploaded {result.Uploaded}, downloaded {result.Downloaded} recordings in {clock.ElapsedMilliseconds} ms");
            }
            _failures = 0;
            _problem = null;
            _lastSyncedAt = DateTimeOffset.Now;
            return true;
        }
        catch (SyncUnauthorizedException)
        {
            Log.Warn("sync: the server no longer accepts this PC's sign-in; sign in again");
            _account.Clear();
            _problem = "Sign in again: Sidgrove Intelligence no longer recognises this PC.";
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or JsonException or TimeoutException)
        {
            _failures++;
            Log.Warn($"sync: failed ({_failures} in a row), trying again in {Backoff(_failures).TotalSeconds:0} s: {e.GetType().Name}: {e.Message}");
            _problem = "Couldn't reach Sidgrove Intelligence. Trying again shortly.";
            return false;
        }
#pragma warning disable CA1031 // The one place that must catch everything: sync runs on its own and may not take the app down.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _failures++;
            Log.Error("sync: run failed", e);
            _problem = "Sync hit a problem. Trying again shortly.";
            return false;
        }
        finally
        {
            _running = false;
            _gate.Release();
            Raise();
        }
    }

    private void Raise() => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }
        _stop.Dispose();
        _wake.Dispose();
        // _gate is left undisposed: a SyncNowAsync started by the UI may still be releasing it.
    }
}
