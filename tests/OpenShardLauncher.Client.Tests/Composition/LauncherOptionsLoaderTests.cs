using OpenShardLauncher.Client.Composition;
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

    public void Dispose() => _launcherFolder.Delete(recursive: true);
}
