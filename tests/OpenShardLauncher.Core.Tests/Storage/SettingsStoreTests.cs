using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Core.Tests.Storage;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_and_is_kept_aside()
    {
        var dataFolder = LauncherDataFolder.Resolve(_temp.Path, "unused", NullLogger.Instance);
        File.WriteAllText(dataFolder.SettingsFile, "{ \"installPath\": ");
        var store = new SettingsStore(dataFolder, NullLogger<SettingsStore>.Instance);

        var settings = store.Load();

        Assert.Equal(new UserSettings(), settings);
        Assert.True(File.Exists(dataFolder.SettingsFile + ".corrupt"));
    }

    public void Dispose() => _temp.Dispose();
}
