using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Client.Services;

// The player's settings for this run, loaded once from settings.json. The settings dialog (phase 6) saves through it.
public sealed class SettingsService(SettingsStore store)
{
    private readonly Lazy<UserSettings> _loaded = new(store.Load);

    public UserSettings Current => _loaded.Value;
}
