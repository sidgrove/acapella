using Murmur.Dictionary;
using Murmur.Core.Sync;

namespace Murmur.Core;

/// <summary>
/// Brings back dictionary entries a sandboxed copy of the app wrote into another app's
/// package cache.
/// </summary>
/// <remarks>
/// <para>
/// Until <see cref="AppDataRedirect"/> learnt to see it, a copy launched from Claude or
/// Codex could run for days with its writes redirected into that app's
/// <c>Packages\&lt;family&gt;\LocalCache\Local\Acapella</c>. Every word added or learnt there
/// was invisible to the real copy. This runs at startup in the real copy and adds whatever
/// is missing, so a PC that had the problem recovers on its first launch of a fixed build.
/// </para>
/// <para>
/// A stranded file is renamed once merged. Otherwise a word deleted on purpose would come
/// back on every launch.
/// </para>
/// </remarks>
public static class StrandedDictionaries
{
    /// <summary>
    /// Merges every stranded dictionary into <paramref name="dictionary"/> and renames each
    /// one it read. Returns how many entries were added.
    /// </summary>
    /// <param name="localAppData">The <c>%LOCALAPPDATA%</c> folder.</param>
    /// <param name="dictionary">The real dictionary.</param>
    /// <param name="now">Stamps the renamed files.</param>
    public static int Recover(string localAppData, DictionaryFile dictionary, DateTimeOffset now)
    {
        var packages = Path.Combine(localAppData, "Packages");
        var folder = Path.GetDirectoryName(dictionary.FilePath);
        if (folder is null || !Directory.Exists(packages)) return 0;

        var relative = Path.Combine(Path.GetRelativePath(localAppData, folder), Path.GetFileName(dictionary.FilePath));
        var known = new HashSet<string>(dictionary.Entries.Select(SyncKinds.DictionaryKey), StringComparer.Ordinal);
        var added = new List<DictionaryEntry>();

        foreach (var package in Directory.EnumerateDirectories(packages))
        {
            var stranded = Path.Combine(package, "LocalCache", "Local", relative);
            if (!File.Exists(stranded)) continue;

            try
            {
                var fresh = DictionaryFile.Parse(File.ReadAllText(stranded)).Where(e => known.Add(SyncKinds.DictionaryKey(e))).ToList();
                File.Move(stranded, Path.ChangeExtension(stranded, $"recovered-{now:yyyyMMdd-HHmmss}.txt"));
                added.AddRange(fresh);
                Log.Info($"dictionary: recovered {fresh.Count} of the entries stranded in the {Path.GetFileName(package)} package");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"dictionary: could not recover the copy stranded in {Path.GetFileName(package)}: {e.Message}");
            }
        }

        if (added.Count > 0) dictionary.Edit(entries => entries.Concat(added));
        return added.Count;
    }
}
