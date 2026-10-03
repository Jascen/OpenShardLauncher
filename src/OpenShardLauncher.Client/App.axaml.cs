using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenShardLauncher.Client.Views;

namespace OpenShardLauncher.Client;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { Title = ReadTitle() };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Placeholder until phase 5 binds the embedded launcher.json to LauncherOptions through the host.
    private static string ReadTitle()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("launcher.json")
            ?? throw new InvalidOperationException("The embedded launcher.json is missing.");
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("Title").GetString() ?? "";
    }
}
