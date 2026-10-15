using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// Recovering dictionary entries a sandboxed copy wrote into another app's package cache.
/// </summary>
public sealed class StrandedDictionariesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _localAppData = Path.Combine(Path.GetTempPath(), $"murmur-stranded-{Guid.NewGuid():N}");
    private readonly DictionaryFile _dictionary;

    public StrandedDictionariesTests()
    {
        var root = Path.Combine(_localAppData, "Acapella");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "dictionary.txt"), "Reflect -> Reflekt\n");
        _dictionary = new DictionaryFile(Path.Combine(root, "dictionary.txt"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_localAppData)) Directory.Delete(_localAppData, recursive: true);
    }

    private string Strand(string package, string text)
    {
        var folder = Path.Combine(_localAppData, "Packages", package, "LocalCache", "Local", "Acapella");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "dictionary.txt");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Adds_the_entries_the_real_dictionary_is_missing()
    {
        Strand("Claude_pzs8sxrjxfjjc", "# Acapella dictionary\n\nZero Jack -> xero Jax\nSidgrove\n# off: old -> Old\n");

        StrandedDictionaries.Recover(_localAppData, _dictionary, Now).ShouldBe(3);

        _dictionary.Entries.Select(e => e.ToFileLine()).ShouldBe(
            ["Reflect -> Reflekt", "Zero Jack -> xero Jax", "Sidgrove", "# off: old -> Old"]);
        new DictionaryFile(_dictionary.FilePath).Entries.Count.ShouldBe(4);
    }

    [Fact]
    public void Skips_entries_already_there_the_way_sync_matches_them()
    {
        // Sync keys a correction on what is heard regardless of case, so this is the same entry.
        Strand("Claude_pzs8sxrjxfjjc", "REFLECT -> Reflekt\nSidgrove\n");
        Strand("OpenAI.Codex_abc", "Sidgrove\nAcapella\n");

        StrandedDictionaries.Recover(_localAppData, _dictionary, Now).ShouldBe(2);

        _dictionary.Entries.Select(e => e.ToFileLine()).ShouldBe(["Reflect -> Reflekt", "Sidgrove", "Acapella"], ignoreOrder: true);
    }

    [Fact]
    public void Renames_what_it_read_so_a_deleted_word_stays_deleted()
    {
        var stranded = Strand("Claude_pzs8sxrjxfjjc", "Sidgrove\n");

        StrandedDictionaries.Recover(_localAppData, _dictionary, Now);
        _dictionary.Edit(entries => entries.Where(e => e.Write != "Sidgrove"));

        File.Exists(stranded).ShouldBeFalse();
        File.Exists(Path.ChangeExtension(stranded, "recovered-20261015-120000.txt")).ShouldBeTrue();
        StrandedDictionaries.Recover(_localAppData, _dictionary, Now).ShouldBe(0);
        _dictionary.Entries.ShouldNotContain(e => e.Write == "Sidgrove");
    }

    [Fact]
    public void Leaves_the_dictionary_alone_when_nothing_is_stranded()
    {
        Directory.CreateDirectory(Path.Combine(_localAppData, "Packages", "Other_123"));
        var before = File.ReadAllText(_dictionary.FilePath);

        StrandedDictionaries.Recover(_localAppData, _dictionary, Now).ShouldBe(0);

        File.ReadAllText(_dictionary.FilePath).ShouldBe(before);
    }
}
