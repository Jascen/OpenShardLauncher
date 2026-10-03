using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Core.Tests.Storage;

public sealed class LauncherDataFolderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private string LauncherFolder => _temp.Combine("launcher");

    private string AppDataRoot => _temp.Combine("appdata");

    private string PortableFolder => Path.Combine(LauncherFolder, LauncherDataFolder.PortableFolderName);

    [Fact]
    public void Portable_when_the_launcher_folder_is_writable()
    {
        Directory.CreateDirectory(LauncherFolder);

        var folder = LauncherDataFolder.Resolve(LauncherFolder, "MyShard", NullLogger.Instance, AppDataRoot);

        Assert.True(folder.IsPortable);
        Assert.Equal(PortableFolder, folder.Path);
    }

    [Fact]
    public void AppData_when_the_launcher_folder_is_not_writable()
    {
        var folder = LauncherDataFolder.Resolve(LauncherFolder, "MyShard", NullLogger.Instance, AppDataRoot, isWritable: _ => false);

        Assert.False(folder.IsPortable);
        Assert.Equal(Path.Combine(AppDataRoot, "MyShard"), folder.Path);
    }

    [Fact]
    public void An_existing_portable_settings_file_wins()
    {
        Directory.CreateDirectory(PortableFolder);
        File.WriteAllText(Path.Combine(PortableFolder, LauncherDataFolder.SettingsFileName), "{}");

        var folder = LauncherDataFolder.Resolve(LauncherFolder, "MyShard", NullLogger.Instance, AppDataRoot, isWritable: _ => false);

        Assert.True(folder.IsPortable);
        Assert.Equal(PortableFolder, folder.Path);
    }

    public void Dispose() => _temp.Dispose();
}
