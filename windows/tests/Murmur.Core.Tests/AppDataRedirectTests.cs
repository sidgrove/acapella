using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// Spotting a copy whose app-data writes land in another app's package cache.
/// </summary>
/// <remarks>
/// Real redirection needs a packaged parent, so these stand in for it: a probe "turns up"
/// in a package cache when the test has already put a file of that name there.
/// </remarks>
public sealed class AppDataRedirectTests : IDisposable
{
    private readonly string _localAppData = Path.Combine(Path.GetTempPath(), $"murmur-redirect-{Guid.NewGuid():N}");
    private string Root => Path.Combine(_localAppData, "Acapella");

    public void Dispose()
    {
        if (Directory.Exists(_localAppData)) Directory.Delete(_localAppData, recursive: true);
    }

    [Fact]
    public void Names_the_package_whose_cache_holds_the_probe()
    {
        var cache = Path.Combine(_localAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Local", "Acapella");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, ".probe-1"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_localAppData, "Packages", "Other_123"));

        AppDataRedirect.FindRedirectingPackage(_localAppData, Root, ".probe-1").ShouldBe("Claude_pzs8sxrjxfjjc");
    }

    [Fact]
    public void Says_nothing_when_no_package_cache_holds_the_probe()
    {
        // A cache with the app's folder but an older probe: the copy that wrote it is gone.
        var cache = Path.Combine(_localAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Local", "Acapella");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, ".probe-old"), string.Empty);

        AppDataRedirect.FindRedirectingPackage(_localAppData, Root, ".probe-2").ShouldBeNull();
    }

    [Fact]
    public void Says_nothing_when_there_are_no_packages_at_all()
    {
        AppDataRedirect.FindRedirectingPackage(_localAppData, Root, ".probe-3").ShouldBeNull();
    }

    [Fact]
    public void Leaves_no_probe_behind()
    {
        Directory.CreateDirectory(Path.Combine(_localAppData, "Packages"));

        AppDataRedirect.FindRedirectingPackage(_localAppData, Root, ".probe-4");

        File.Exists(Path.Combine(Root, ".probe-4")).ShouldBeFalse();
    }
}
