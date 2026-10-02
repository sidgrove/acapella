using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core.Sync;

namespace Murmur.App.Views;

/// <summary>
/// The Sync card's body: who is signed in and when the last sync was, with Sign in, Sync now
/// and Sign out.
/// </summary>
public sealed class SyncPart : UserControl
{
    private readonly SyncService? _sync;
    private readonly StatusDot _dot;
    private readonly TextBlock _status;
    private readonly TextBlock _when;
    private readonly TextBlock _detail;
    private readonly TextBlock _problem;
    private readonly SgButton _signIn;
    private readonly SgButton _syncNow;
    private readonly SgButton _signOut;
    private readonly DispatcherTimer _tick = new() { Interval = Tokens.Motion.RelativeTimeTick };
    private bool _waiting;

    /// <summary>Builds the section over <paramref name="sync"/>, or an inert one in a preview.</summary>
    public SyncPart(SyncService? sync)
    {
        _sync = sync;
        _dot = new StatusDot { VerticalAlignment = VerticalAlignment.Center };
        _status = Text.BodyStrong(string.Empty);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _when = Text.Meta(string.Empty);
        _when.VerticalAlignment = VerticalAlignment.Center;
        _detail = Text.Meta(string.Empty);
        _problem = Text.Meta(string.Empty);
        _problem.Foreground = Tokens.Accent.Coral.Ink;

        _signIn = new SgButton("Sign in with Sidgrove", SgButton.Kind.Primary, compact: true, icon: Icons.Cloud);
        _signIn.Click += async (_, _) =>
        {
            if (_sync is null) return;
            _waiting = true;
            Refresh();
            try
            {
                await _sync.SignInAsync(CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                _waiting = false;
                Refresh();
            }
        };

        _syncNow = new SgButton("Sync now", SgButton.Kind.Ghost, compact: true, icon: Icons.Refresh);
        _syncNow.Click += (_, _) => _ = _sync?.SyncNowAsync();

        _signOut = new SgButton("Sign out", SgButton.Kind.Quiet, compact: true, icon: Icons.Power);
        _signOut.Click += async (_, _) =>
        {
            if (_sync is null) return;
            _signOut.IsEnabled = false;
            try
            {
                await _sync.SignOutAsync().ConfigureAwait(true);
            }
            finally
            {
                _signOut.IsEnabled = true;
            }
        };

        Content = Panels.Column(Tokens.Space.Base,
            Panels.Row(Tokens.Space.Snug, _dot, _status, _when),
            _detail,
            _problem,
            Panels.Row(Tokens.Space.Snug, _signIn, _syncNow, _signOut));

        _tick.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) =>
        {
            if (_sync is not null) _sync.StatusChanged += OnStatusChanged;
            _tick.Start();
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_sync is not null) _sync.StatusChanged -= OnStatusChanged;
            _tick.Stop();
        };
        Refresh();
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    /// <summary>Re-reads the status.</summary>
    public void Refresh()
    {
        var status = _sync?.Status;
        var signedIn = status?.IsSignedIn == true;

        _dot.Fill = signedIn ? Tokens.Accent.Emerald.Ink : Tokens.Brushes.AmberMid;
        _dot.IsLive = status?.IsRunning == true || _waiting;

        if (signedIn)
        {
            _status.Text = $"Signed in as {status!.Email}";
            _when.Text = status.IsRunning ? "· syncing now"
                : status.LastSyncedAt is null ? "· not synced yet"
                : $"· last synced {Relative(status.LastSyncedAt, DateTimeOffset.Now)}";
            _detail.Text = string.Empty;
        }
        else
        {
            _status.Text = "Not signed in";
            _when.Text = string.Empty;
            _detail.Text = _waiting
                ? "Finish signing in in your browser. This waits for up to five minutes."
                : status?.CanSignIn == true
                    ? "Sign in to keep your dictionary, learnt fixes, history, settings and corrected recordings the same on every PC."
                    : "Sync isn't available here.";
        }

        _when.IsVisible = _when.Text.Length > 0;
        _detail.IsVisible = _detail.Text.Length > 0;
        _problem.Text = status?.Problem ?? string.Empty;
        _problem.IsVisible = !string.IsNullOrEmpty(_problem.Text) && !_waiting;

        _signIn.IsVisible = !signedIn;
        _signIn.IsEnabled = status?.CanSignIn == true && !_waiting;
        _syncNow.IsVisible = signedIn;
        _syncNow.IsEnabled = status?.IsRunning != true;
        _signOut.IsVisible = signedIn;
    }

    /// <summary>"just now", "4 minutes ago", "2 hours ago", "on 01/10/2026", or "never".</summary>
    public static string Relative(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } when) return "never";
        var ago = now - when;
        if (ago < TimeSpan.FromMinutes(1)) return "just now";
        if (ago < TimeSpan.FromHours(1)) return Plural((int)ago.TotalMinutes, "minute");
        if (ago < TimeSpan.FromDays(1)) return Plural((int)ago.TotalHours, "hour");
        return "on " + when.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n.ToString(CultureInfo.InvariantCulture)} {unit}s ago";
    }
}
