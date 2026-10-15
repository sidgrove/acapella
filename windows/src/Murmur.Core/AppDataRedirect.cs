using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Finds out whether this process's writes to app data are being redirected into another
/// app's package cache.
/// </summary>
/// <remarks>
/// <para>
/// A process started by a packaged (MSIX) app can have its writes under
/// <c>%LOCALAPPDATA%</c> quietly redirected into that app's
/// <c>Packages\&lt;family&gt;\LocalCache\Local</c>. Reads see the redirected copy too, so from
/// inside, everything looks normal: the app simply runs on a stale settings file and an
/// empty dictionary, and whatever it learns is lost to every other launch.
/// </para>
/// <para>
/// Package identity is not a usable signal. A copy launched from Claude's desktop app on
/// 2026-10-10 had none (<c>GetCurrentPackageFamilyName</c> said no package) and was
/// redirected all the same, which is how a week of dictionary entries vanished. So the
/// only evidence trusted is a probe file: write it to the data folder, then look for it in
/// every package's cache.
/// </para>
/// </remarks>
public static class AppDataRedirect
{
    /// <summary>
    /// Writes a probe into <paramref name="root"/> and returns the package family whose
    /// cache it landed in, or null when the write went where it says.
    /// </summary>
    /// <param name="localAppData">The <c>%LOCALAPPDATA%</c> folder.</param>
    /// <param name="root">The data folder, normally <see cref="AppPaths.Root"/>.</param>
    /// <param name="probeName">A file name unique to this process.</param>
    public static string? FindRedirectingPackage(string localAppData, string root, string probeName)
    {
        var packages = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packages)) return null;

        var relative = Path.GetRelativePath(localAppData, root);
        var probe = Path.Combine(root, probeName);

        Directory.CreateDirectory(root);
        File.WriteAllText(probe, string.Empty);
        try
        {
            foreach (var package in Directory.EnumerateDirectories(packages))
            {
                if (File.Exists(Path.Combine(package, "LocalCache", "Local", relative, probeName)))
                    return Path.GetFileName(package);
            }
            return null;
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
