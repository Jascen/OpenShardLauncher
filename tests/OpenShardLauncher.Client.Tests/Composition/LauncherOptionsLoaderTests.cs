using System.Text.Json;
using OpenShardLauncher.Client.Composition;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Client.Tests.Composition;

public sealed class LauncherOptionsLoaderTests : IDisposable
{
    private readonly DirectoryInfo _launcherFolder = Directory.CreateTempSubdirectory("osl-options-");

    [Fact]
    public void A_local_file_is_layered_over_launcher_json_only_when_asked()
    {
        File.WriteAllText(Path.Combine(_launcherFolder.FullName, LauncherOptionsLoader.LocalFileName), """
            {
              "AllowUnsignedFeed": true,
              "TrustedPublicKeys": [ { "alg": "p256", "key": "dGVzdA==" } ]
            }
            """);

        var debug = LauncherOptionsLoader.Load(_launcherFolder.FullName, includeLocalFile: true);
        var release = LauncherOptionsLoader.Load(_launcherFolder.FullName, includeLocalFile: false);

        Assert.True(debug.AllowUnsignedFeed);
        Assert.Equal(new TrustedKey("p256", "dGVzdA=="), Assert.Single(debug.TrustedPublicKeys));
        Assert.Equal(TimeSpan.FromHours(4), debug.PackageCheckInterval); // Still from the embedded launcher.json
        Assert.False(release.AllowUnsignedFeed);
        Assert.Empty(release.TrustedPublicKeys);
    }

    // A fork's launcher.json may leave properties out; they keep LauncherOptions' defaults, not false/0/null.
    [Fact]
    public void Properties_left_out_of_launcher_json_keep_their_defaults()
    {
        var empty = JsonSerializer.Deserialize("{}", LauncherOptionsJsonContext.Default.LauncherOptions)!;
        var defaults = new LauncherOptions();

        Assert.Equal(defaults.UpdateUrl, empty.UpdateUrl);
        Assert.Equal(defaults.AppDataFolderName, empty.AppDataFolderName);
        Assert.Equal(defaults.DefaultInstallFolder, empty.DefaultInstallFolder);
        Assert.Equal(defaults.PackageCheckInterval, empty.PackageCheckInterval);
        Assert.Empty(empty.TrustedPublicKeys);
        Assert.True(empty.TazUO.Enabled);
        Assert.Equal(defaults.TazUO.InstallFolder, empty.TazUO.InstallFolder);
        Assert.Equal(defaults.TazUO.ExecutableName, empty.TazUO.ExecutableName);

        var partial = JsonSerializer.Deserialize("""{ "TazUO": { "Profiles": [ { "Id": "shard" } ] } }""", LauncherOptionsJsonContext.Default.LauncherOptions)!;
        Assert.True(partial.TazUO.Enabled);
        Assert.Equal(new TazUOProfile { Id = "shard" }, Assert.Single(partial.TazUO.Profiles)); // 127.0.0.1:2593
    }

    public void Dispose() => _launcherFolder.Delete(recursive: true);
}
