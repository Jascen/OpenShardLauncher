using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Infrastructure.Tests.Platform;

// How the client is started: the TazUO way without arguments, directly with substituted arguments otherwise.
public sealed class ClientLauncherTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-client-");

    private string Game => Path.Combine(_root.FullName, "Game");

    [Fact]
    public void WithoutArguments_IsShellExecutedInItsFolder()
    {
        var startInfo = ClientLauncher.CreateStartInfo(Game, new ClientOptions())!;

        Assert.True(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal(Path.Combine(Game, "TazUO"), startInfo.WorkingDirectory);
        Assert.Equal(Path.Combine(Game, "TazUO", "TazUOLauncher" + (OperatingSystem.IsWindows() ? ".exe" : "")), startInfo.FileName);
    }

    [Fact]
    public void Arguments_GetTheFoldersSubstituted_AndAreStartedDirectly()
    {
        var client = new ClientOptions
        {
            InstallFolder = "ClassicUO",
            ExecutableName = "ClassicUO",
            Arguments = ["-uopath", "{GameFolder}", "-plugins", "{ClientFolder}/Assistant/Razor.dll", "-ip", "play.example.com"],
        };

        var startInfo = ClientLauncher.CreateStartInfo(Game, client)!;

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(Path.Combine(Game, "ClassicUO"), startInfo.WorkingDirectory);
        Assert.Equal(
            ["-uopath", Game, "-plugins", Path.Combine(Game, "ClassicUO") + "/Assistant/Razor.dll", "-ip", "play.example.com"],
            startInfo.ArgumentList);
    }

    [Theory]
    [InlineData("../Elsewhere")]
    [InlineData(".openshardlauncher-cache")]
    public void AFolderOutsideTheInstallFolder_IsRefused(string installFolder)
    {
        Assert.Null(ClientLauncher.CreateStartInfo(Game, new ClientOptions { InstallFolder = installFolder }));
    }

    public void Dispose() => _root.Delete(recursive: true);
}
