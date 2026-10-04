using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Infrastructure.Tests.Platform;

// The applier over real temp folders: a launcher folder with the old version and its .temp/staging/ with the new one.
// Waiting for the old process and starting launchers are recorded instead.
public sealed class LauncherSwapTests : IDisposable
{
    private const string Exe = "Launcher.exe";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-swap-");
    private readonly List<(string Exe, string Folder)> _started = [];
    private bool _oldLauncherExits = true;

    public LauncherSwapTests()
    {
        Write(AppDir, Exe, "old exe");
        Write(AppDir, "lib.dll", "old lib");
        Write(AppDir, "player-file.txt", "mine");
        Write(Staging, Exe, "new exe");
        Write(Staging, "lib.dll", "new lib");
        Write(Staging, "runtimes/native.so", "native");
        UpdateResultMarker.WriteExpected(Marker, "1.1.0", "1.0.0");
    }

    private string AppDir => Path.Combine(_root.FullName, "launcher");

    private string Staging => Path.Combine(AppDir, ".temp", "staging");

    private string Marker => Path.Combine(AppDir, LauncherDataFolder.PortableFolderName, LauncherDataFolder.UpdateResultFileName);

    [Fact]
    public void Protocol1_CopiesTheStagedFilesOver_AndStartsTheNewLauncher()
    {
        var result = Apply(SelfUpdaterArguments());

        Assert.Equal(LauncherSwap.Result.Applied, result);
        Assert.Equal("new exe", Read(AppDir, Exe));
        Assert.Equal("new lib", Read(AppDir, "lib.dll"));
        Assert.Equal("native", Read(AppDir, "runtimes/native.so"));
        Assert.Equal("mine", Read(AppDir, "player-file.txt")); // Files the package doesn't have are left alone
        Assert.Equal([(Path.Combine(AppDir, Exe), AppDir)], _started);
        Assert.Equal(new LauncherUpdateReport(true, "1.1.0", null), Take("1.1.0"));
    }

    [Theory]
    [InlineData("2")] // A protocol this version doesn't know
    [InlineData("")]
    public void UnknownProtocol_ChangesNothing_RecordsTheError_AndRestartsTheOldLauncher(string protocol)
    {
        var args = SelfUpdaterArguments().ToArray();
        args[1] = protocol;

        var result = Apply(args);

        Assert.Equal(LauncherSwap.Result.BadArguments, result);
        AssertUnchangedAndOldRestarted();
    }

    [Fact]
    public void BadArguments_ChangeNothing()
    {
        var args = SelfUpdaterArguments().ToArray();
        args[2] = "not-a-pid";

        var result = Apply(args);

        Assert.Equal(LauncherSwap.Result.BadArguments, result);
        AssertUnchangedAndOldRestarted();
    }

    [Fact]
    public void AnOldLauncherThatDoesntExit_ChangesNothing_AndIsReportedOnTheNextStart()
    {
        _oldLauncherExits = false;

        var result = Apply(SelfUpdaterArguments());

        Assert.Equal(LauncherSwap.Result.OldLauncherStillRunning, result);
        var report = AssertUnchangedAndOldRestarted();
        Assert.Contains("10 seconds", report.Error);
    }

    [Fact]
    public void AnApplierRunningFromTheLauncherFolderItself_ChangesNothing()
    {
        var result = new LauncherSwap(AppDir, NullLogger.Instance, WaitForExit, Start, TimeSpan.Zero).Apply(SelfUpdaterArguments());

        Assert.Equal(LauncherSwap.Result.BadArguments, result);
        AssertUnchangedAndOldRestarted();
    }

    public void Dispose() => _root.Delete(recursive: true);

    // Exactly what SelfUpdater starts the applier with.
    private IReadOnlyList<string> SelfUpdaterArguments() => SelfUpdater.Arguments(4242, AppDir, Exe);

    private LauncherSwap.Result Apply(IReadOnlyList<string> args) =>
        new LauncherSwap(Staging, NullLogger.Instance, WaitForExit, Start, TimeSpan.Zero).Apply(args);

    private bool WaitForExit(int pid, TimeSpan timeout)
    {
        Assert.Equal(4242, pid);
        Assert.Equal(TimeSpan.FromSeconds(10), timeout);
        return _oldLauncherExits;
    }

    private void Start(string exe, string folder) => _started.Add((exe, folder));

    // The failure the next start (still the old version) reports.
    private LauncherUpdateReport AssertUnchangedAndOldRestarted()
    {
        Assert.Equal("old exe", Read(AppDir, Exe));
        Assert.Equal("old lib", Read(AppDir, "lib.dll"));
        Assert.False(File.Exists(Path.Combine(AppDir, "runtimes", "native.so")));
        Assert.Equal([(Path.Combine(AppDir, Exe), AppDir)], _started);
        var report = Take("1.0.0");
        Assert.NotNull(report);
        Assert.False(report.Succeeded);
        return report;
    }

    private LauncherUpdateReport? Take(string currentVersion) => UpdateResultMarker.Take(Marker, currentVersion, NullLogger.Instance);

    private static void Write(string folder, string name, string content)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string folder, string name) => File.ReadAllText(Path.Combine(folder, name));
}
