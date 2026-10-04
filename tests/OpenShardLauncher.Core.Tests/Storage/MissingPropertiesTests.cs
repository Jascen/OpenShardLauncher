using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Core.Tests.Storage;

// A file that leaves properties out (hand-edited, or written by an older launcher before a property existed) keeps
// their defaults instead of false/0/null.
public sealed class MissingPropertiesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private LauncherDataFolder DataFolder => LauncherDataFolder.Resolve(_temp.Path, "unused", NullLogger.Instance);

    [Fact]
    public void Settings_left_out_of_settings_json_keep_their_defaults()
    {
        File.WriteAllText(DataFolder.SettingsFile, "{}");

        Assert.Equal(new UserSettings(), new SettingsStore(DataFolder, NullLogger<SettingsStore>.Instance).Load());
    }

    [Fact]
    public void Only_the_settings_in_the_file_change()
    {
        File.WriteAllText(DataFolder.SettingsFile, """{ "allowInsecureDownloads": true }""");

        var settings = new SettingsStore(DataFolder, NullLogger<SettingsStore>.Instance).Load();

        Assert.Equal(new UserSettings { AllowInsecureDownloads = true }, settings);
        Assert.True(settings.VerifyOnLaunch);
    }

    [Fact]
    public void A_feed_state_missing_its_sections_still_works()
    {
        File.WriteAllText(DataFolder.FeedStateFile, "{}");
        var store = new FeedStateStore(DataFolder, NullLogger<FeedStateStore>.Instance);

        Assert.Null(store.GetInstalledVersion("tazuo"));
        store.SetInstalledVersion("tazuo", "2.0.0");
        store.SetLastFileListVersion(new Uri("https://updates.example.com/"), 5);

        Assert.Equal("2.0.0", store.GetInstalledVersion("tazuo"));
        Assert.Equal(5, store.GetLastFileListVersion(new Uri("https://updates.example.com/")));
    }

    public void Dispose() => _temp.Dispose();
}
