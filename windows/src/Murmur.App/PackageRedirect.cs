using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Murmur.Abstractions;
using Murmur.Core;

namespace Murmur.App;

/// <summary>
/// Gets the app out of another app's package container when it was launched from one.
/// </summary>
/// <remarks>
/// <para>
/// A process started by a packaged (MSIX) app inherits that app's identity, and Windows
/// then quietly redirects its writes under <c>%LOCALAPPDATA%</c> into the parent package's
/// private cache. On 2026-09-10 and again on 2026-09-24 the Codex desktop app rebuilt and
/// relaunched Acapella: that copy read a settings file from weeks earlier, had no
/// dictionary and wrote its history where nothing else looks. Accuracy fell and nothing in
/// the real log said why.
/// </para>
/// <para>
/// Identity alone proves nothing: Claude's shell also carries one and its writes land in
/// the real folder. So a probe file is written and looked for in the parent's cache, and
/// only a probe that turns up there counts. The copy then hands itself to Explorer, which
/// starts it as an ordinary desktop process, and exits before claiming the single instance.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class PackageRedirect
{
    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE
    private const int InsufficientBuffer = 122;

    /// <summary>
    /// True when this process's app-data writes are being redirected into another app's
    /// package, in which case a clean copy has been started through Explorer.
    /// </summary>
    public static bool RelaunchedOutsidePackage()
    {
        try
        {
            var family = PackageFamilyName();
            if (family is null || !IsRedirected(family)) return false;

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

    private static string? PackageFamilyName()
    {
        uint length = 0;
        var result = GetCurrentPackageFamilyName(ref length, null);
        if (result == NoPackage || result != InsufficientBuffer) return null;

        var buffer = new char[length];
        return GetCurrentPackageFamilyName(ref length, buffer) == 0 ? new string(buffer, 0, (int)length - 1) : null;
    }

    private static bool IsRedirected(string family)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var name = $".package-probe-{Environment.ProcessId}";
        var probe = Path.Combine(AppPaths.Root, name);
        var redirected = Path.Combine(localAppData, "Packages", family, "LocalCache", "Local", AppPaths.ProductName, name);

        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(probe, string.Empty);
        try
        {
            return File.Exists(redirected);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char[]? packageFamilyName);
}
