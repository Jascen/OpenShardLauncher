using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;

namespace OpenShardLauncher.Client.Services;

// Opens a web page in the player's browser.
public interface IUrlLauncher
{
    Task OpenAsync(Uri uri);
}

public sealed class AvaloniaUrlLauncher(ILogger<AvaloniaUrlLauncher> logger) : IUrlLauncher
{
    public async Task OpenAsync(Uri uri)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null || !await TopLevel.GetTopLevel(window)!.Launcher.LaunchUriAsync(uri))
        {
            logger.LogWarning("Could not open {Url}", uri);
        }
    }
}
