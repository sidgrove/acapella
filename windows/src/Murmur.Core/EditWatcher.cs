using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>What the user made of one typed dictation.</summary>
/// <param name="At">When its key was released, which is also its history record's time.</param>
/// <param name="Typed">What was typed.</param>
/// <param name="Final">The field's version of it when the watch ended.</param>
/// <param name="Changes">Runs of words the user replaced.</param>
/// <param name="WordErrorRate">Changed words over the words the user left.</param>
public sealed record DictationEdit(DateTimeOffset At, string Typed, string Final, IReadOnlyList<WordChange> Changes, double WordErrorRate)
{
    /// <summary>Whether the user changed any word.</summary>
    public bool IsEdited => WordErrorRate > 0 || Changes.Count > 0;
}

/// <summary>
/// After a dictation is typed, reads the field back now and then to see what the user made
/// of it, until they send it, move away, dictate again or two minutes pass.
/// </summary>
/// <remarks>
/// <para>
/// What the user leaves is the best judge of what should have been typed, and the only one
/// that costs nothing: every edit is a measured mistake and a sound-alike fix is a dictionary
/// entry the user has already written once.
/// </para>
/// <para>
/// Read only, through the same UI Automation read as the caret context, and only while the
/// field that took the text still has focus. It stops the moment a new recording starts, so
/// it never competes with the read made at key-down. Nothing it reads leaves the machine.
/// </para>
/// </remarks>
public sealed class EditWatcher
{
    private readonly ITextInjector _injector;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _watch;
    private Task _current = Task.CompletedTask;

    /// <summary>Creates a watcher that reads through <paramref name="injector"/>.</summary>
    public EditWatcher(ITextInjector injector) => _injector = injector;

    /// <summary>How soon after typing the first read is made, to confirm the text landed.</summary>
    public TimeSpan FirstLook { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>How often the field is read after that.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a dictation is watched at most.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Characters read either side of the caret beyond the length of the dictation.</summary>
    public const int Margin = 200;

    /// <summary>Raised when a watch ends having found the text at least once. On a pool thread.</summary>
    public event EventHandler<DictationEdit>? Finished;

    /// <summary>The watch in progress, or a completed task. For tests and shutdown.</summary>
    public Task Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>Starts watching <paramref name="typed"/> in the field <paramref name="target"/>, ending any earlier watch.</summary>
    /// <param name="at">The dictation's release time, which identifies it in the history.</param>
    /// <param name="typed">What was typed, without any joining space or full stop.</param>
    /// <param name="target">The field's identity from <see cref="ITextInjector.FocusTarget"/>; null means it cannot be told apart, and nothing is watched.</param>
    public void Watch(DateTimeOffset at, string typed, string? target)
    {
        if (target is null || string.IsNullOrWhiteSpace(typed)) return;

        var watch = new CancellationTokenSource();
        lock (_lock)
        {
            _watch?.Cancel();
            _watch = watch;
            _current = Task.Run(() => RunAsync(at, typed, target, watch.Token), CancellationToken.None);
        }
    }

    /// <summary>Ends the current watch now, keeping what it last saw. Called at every key-down.</summary>
    public void Stop()
    {
        lock (_lock) _watch?.Cancel();
    }

    private async Task RunAsync(DateTimeOffset at, string typed, string target, CancellationToken cancellationToken)
    {
        string? lastSeen = null;
        var reason = "two minutes passed";
        var misses = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var delay = FirstLook;
            while (clock.Elapsed < Duration)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = Interval;

                if (_injector.FocusTarget != target)
                {
                    reason = "focus moved";
                    break;
                }

                var around = await _injector.ReadTextAroundCaretAsync(typed.Length + Margin, typed.Length + Margin, cancellationToken).ConfigureAwait(false);
                if (around is null)
                {
                    if (++misses >= 3 && lastSeen is null)
                    {
                        reason = "the field cannot be read";
                        break;
                    }
                    continue;
                }

                if (EditAlignment.Find(typed, around) is not { } found)
                {
                    reason = lastSeen is null ? "never found in the field" : "sent or cleared";
                    break;
                }
                lastSeen = found;
            }
        }
        catch (OperationCanceledException)
        {
            reason = "the next dictation started";
        }
        catch (Exception e)
        {
            Log.Warn($"edit check failed: {e.Message}");
        }

        if (lastSeen is null)
        {
            Log.Info($"edit check: nothing read ({reason})");
            return;
        }

        var changes = EditAlignment.Changes(typed, lastSeen);
        var edit = new DictationEdit(at, typed, lastSeen, changes, EditAlignment.WordErrorRate(typed, lastSeen));
        Log.Info(edit.IsEdited
            ? $"edit check: {edit.WordErrorRate:P0} of words changed{(changes.Count > 0 ? ": " + string.Join("; ", changes.Select(c => $"{c.Typed} -> {c.Final}")) : string.Empty)} ({reason})"
            : $"edit check: left as typed ({reason})");

        try { Finished?.Invoke(this, edit); }
        catch (Exception e) { Log.Warn($"edit check could not be recorded: {e.Message}"); }
    }
}
