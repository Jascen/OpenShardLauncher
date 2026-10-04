using Avalonia;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Client;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Started by the previous version as the self-update applier (from .temp/staging/): no window, it replaces the
        // launcher's files and starts it again.
        if (LauncherSwap.IsApplyRequest(args))
        {
            return LauncherSwap.Run(args);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
