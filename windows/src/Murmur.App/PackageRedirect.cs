using System.Diagnostics;
using System.Runtime.Versioning;
using Murmur.Abstractions;
using Murmur.Core;

namespace Murmur.App;

/// <summary>
/// Gets the app out of another app's package container when it was launched from one.
/// </summary>
/// <remarks>
/// <para>
/// A process started by a packaged (MSIX) app can have its writes under
/// <c>%LOCALAPPDATA%</c> quietly redirected into the parent package's private cache. On
/// 2026-09-10 and again on 2026-09-24 the Codex desktop app rebuilt and relaunched
/// Acapella: that copy read a settings file from weeks earlier, had no dictionary and wrote
/// its history where nothing else looks. Accuracy fell and nothing in the real log said why.
/// </para>
/// <para>
/// This check first asked for package identity and stopped when there was none. Copies
/// launched from Claude's desktop app have none and are redirected anyway, so from
/// 2026-09-27 to 2026-10-10 every relaunch from a Claude session ran in Claude's cache and
/// took the dictionary edits made there with it. <see cref="AppDataRedirect"/> now trusts
/// only a probe file. When it turns up in a package cache, the copy hands itself to
/// Explorer, which starts it as an ordinary desktop process, and exits before claiming the
/// single instance.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class PackageRedirect
{
    /// <summary>
    /// True when this process's app-data writes are being redirected into another app's
    /// package, in which case a clean copy has been started through Explorer.
    /// </summary>
    public static bool RelaunchedOutsidePackage()
    {
        try
        {
            var family = AppDataRedirect.FindRedirectingPackage(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppPaths.Root,
                $".package-probe-{Environment.ProcessId}");
            if (family is null) return false;

            Log.Warn($"started inside the {family} package, whose writes to app data are redirected; relaunching through Explorer");
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Environment.ProcessPath}\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception e)
        {
            // Never stop the app starting over this. The worst case is the old behaviour.
            Log.Warn($"package check failed: {e.Message}");
            return false;
        }
    }
}
