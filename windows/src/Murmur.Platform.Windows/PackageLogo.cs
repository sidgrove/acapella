using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Murmur.Platform.Windows;

/// <summary>
/// Finds the logo of a Store (MSIX) app from its executable's path: the Square44x44Logo the
/// package's AppxManifest.xml names, at the asset size that best fits.
/// </summary>
/// <remarks>
/// The manifest names <c>Assets\AppList.png</c>, but that file rarely exists; Windows picks
/// between qualified copies such as <c>AppList.targetsize-48_altform-unplated.png</c>. The
/// unplated ones are the bare logo with no background plate, which is what sits in a mark.
/// </remarks>
internal static partial class PackageLogo
{
    /// <summary>The logo file for the app at <paramref name="exePath"/>, or null when it is not packaged.</summary>
    public static string? Find(string exePath, int size)
    {
        var root = PackageRoot(exePath);
        if (root is null) return null;

        var logo = XDocument.Load(Path.Combine(root, "AppxManifest.xml"))
            .Descendants()
            .Select(e => (string?)e.Attribute("Square44x44Logo"))
            .FirstOrDefault(a => a is { Length: > 0 });
        if (logo is null) return null;

        var full = Path.Combine(root, logo);
        var folder = Path.GetDirectoryName(full);
        if (folder is null || !Directory.Exists(folder)) return null;
        return Pick(Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>(), Path.GetFileName(full), size) is { } name
            ? Path.Combine(folder, name)
            : null;
    }

    /// <summary>
    /// Chooses from <paramref name="files"/> the copy of <paramref name="logo"/> to show at
    /// <paramref name="size"/> pixels: the smallest unplated target size that is big enough, then
    /// a plated one, then the largest scale, then the file as named.
    /// </summary>
    public static string? Pick(IEnumerable<string> files, string logo, int size)
    {
        var stem = Path.GetFileNameWithoutExtension(logo);
        var extension = Path.GetExtension(logo);
        var targets = new List<(int Size, bool Unplated, string Name)>();
        var scales = new List<(int Scale, string Name)>();
        var exact = false;

        foreach (var name in files)
        {
            if (name.Equals(logo, StringComparison.OrdinalIgnoreCase)) { exact = true; continue; }
            if (!name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
            var qualifiers = name[(stem.Length + 1)..^extension.Length];
            if (TargetSize().Match(qualifiers) is { Success: true } t)
                targets.Add((int.Parse(t.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), t.Groups[2].Success, name));
            else if (Scale().Match(qualifiers) is { Success: true } s)
                scales.Add((int.Parse(s.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), name));
        }

        var best = targets
            .OrderByDescending(t => t.Unplated)
            .ThenBy(t => t.Size >= size ? 0 : 1)
            .ThenBy(t => t.Size >= size ? t.Size : -t.Size)
            .Select(t => t.Name)
            .FirstOrDefault();
        return best
            ?? scales.OrderByDescending(s => s.Scale).Select(s => s.Name).FirstOrDefault()
            ?? (exact ? logo : null);
    }

    private static string? PackageRoot(string exePath)
    {
        for (var folder = Path.GetDirectoryName(exePath); folder is not null; folder = Path.GetDirectoryName(folder))
            if (File.Exists(Path.Combine(folder, "AppxManifest.xml"))) return folder;
        return null;
    }

    [GeneratedRegex(@"^targetsize-(\d+)(_altform-unplated)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TargetSize();

    [GeneratedRegex(@"^scale-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Scale();
}
