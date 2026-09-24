using Avalonia;

namespace Murmur.App;

/// <summary>Entry point.</summary>
public static class Program
{
    /// <summary>Starts the app, or runs a headless self-test.</summary>
    /// <param name="args">
    /// Command line. <c>--selftest</c> exits without showing UI; <c>--minimized</c> starts
    /// in the tray, which is what the sign-in registration passes.
    /// </param>
    /// <returns>0 on success.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        PlatformFactory.InstallResolver();

        // The published single-file exe is the only artifact CI can run end to end, and a
        // GitHub runner cannot show a window. This branch exercises startup — assembly
        // loading, native library resolution out of the self-extracted bundle, model
        // discovery — and exits, which is the class of failure that only appears after
        // publishing.
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTest.Run();
        }

        // Launched by a packaged app such as Codex, every write to app data would land in
        // that app's private cache. Hand over to a clean copy before claiming the instance.
        if (OperatingSystem.IsWindows() && PackageRedirect.RelaunchedOutsidePackage()) return 0;

        App.StartMinimized = args.Contains(App.MinimizedArgument, StringComparer.OrdinalIgnoreCase);

        // One copy per user session. A second launch hands over to the first — which
        // brings its window back — and exits.
        using var instance = SingleInstance.TryClaim();
        if (instance is null) return 0;

        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Configures Avalonia. Also used by the headless test host.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
