using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace OpenShardLauncher.Client.Services;

// Ends the launcher, e.g. so a self-update applier can replace it (phase 7).
public interface IAppLifetime
{
    void Shutdown();
}

public sealed class AvaloniaAppLifetime : IAppLifetime
{
    public void Shutdown() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
