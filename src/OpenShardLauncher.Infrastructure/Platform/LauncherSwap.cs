using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Logging;

namespace OpenShardLauncher.Infrastructure.Platform;

// The applier: runs in the *new* version's exe, from .temp/staging/, started by the old launcher (SelfUpdater) with
//   --apply-update <protocol> <pid> <appDir> <exeName>
// It waits for the old launcher to exit, copies its own staging folder over the launcher folder (exe last, so a copy
// that fails partway still leaves the old exe in place) and starts the result. On an unknown protocol, bad arguments
// or an old launcher that doesn't exit within 10 seconds it changes nothing, records the error in the result marker
// and starts the old launcher again. There is no window: the next start reports the result (UpdateResultMarker).
public sealed class LauncherSwap
{
    public static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _stagingFolder;
    private readonly ILogger _logger;
    private readonly Func<int, TimeSpan, bool> _waitForExit;
    private readonly Action<string, string> _start;
    private readonly TimeSpan _retryDelay;

    // waitForExit(pid, timeout) is false when the process is still running; start(exePath, workingFolder) starts a
    // launcher. Both are replaced in tests.
    public LauncherSwap(
        string stagingFolder,
        ILogger logger,
        Func<int, TimeSpan, bool>? waitForExit = null,
        Action<string, string>? start = null,
        TimeSpan? retryDelay = null)
    {
        _stagingFolder = Path.GetFullPath(stagingFolder);
        _logger = logger;
        _waitForExit = waitForExit ?? WaitForProcessExit;
        _start = start ?? StartProcess;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(250);
    }

    public enum Result
    {
        Applied = 0,
        BadArguments = 1,
        OldLauncherStillRunning = 2,
        CopyFailed = 3,
    }

    public static bool IsApplyRequest(string[] args) => args.Length > 0 && args[0] == SelfUpdater.ApplyArgument;

    // Program.Main's --apply-update branch. Logs to the launcher folder's data folder when the arguments name one.
    public static int Run(string[] args)
    {
        using var loggers = LoggerFactory.Create(logging =>
        {
            if (args.Length > 3 && Path.IsPathFullyQualified(args[3]) && Directory.Exists(args[3]))
            {
                logging.AddLauncherFileLogging(Path.Combine(args[3], LauncherDataFolder.PortableFolderName, "logs"));
            }
        });
        var logger = loggers.CreateLogger<LauncherSwap>();
        return (int)new LauncherSwap(AppContext.BaseDirectory, logger).Apply(args);
    }

    public Result Apply(IReadOnlyList<string> args)
    {
        var target = ParseTarget(args);
        if (args.Count < 2 || args[1] != SelfUpdater.ProtocolVersion.ToString(CultureInfo.InvariantCulture))
        {
            return Abandon(target, Result.BadArguments, $"Unknown self-update protocol '{(args.Count > 1 ? args[1] : "")}'.");
        }

        if (args.Count != 5 || target is null
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
        {
            return Abandon(target, Result.BadArguments, "The self-update arguments are invalid.");
        }

        var (appDir, exeName) = target.Value;
        if (SelfUpdater.SamePath(appDir, _stagingFolder) || !File.Exists(Path.Combine(_stagingFolder, exeName)))
        {
            return Abandon(target, Result.BadArguments, $"The applier isn't running from a staged copy of {exeName}.");
        }

        _logger.LogInformation("Applying the launcher update in {Folder}; waiting for process {Pid} to exit", appDir, pid);
        if (!_waitForExit(pid, ExitTimeout))
        {
            return Abandon(target, Result.OldLauncherStillRunning, $"The old launcher didn't exit within {ExitTimeout.TotalSeconds:0} seconds.");
        }

        try
        {
            CopyOver(appDir, exeName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "Could not copy the new launcher into {Folder}", appDir);
            return Abandon(target, Result.CopyFailed, $"Could not replace the launcher's files: {e.Message}");
        }

        _logger.LogInformation("Applied the launcher update; starting {Exe}", exeName);
        Restart(appDir, exeName);
        return Result.Applied;
    }

    // Everything in staging/ over appDir, the exe last.
    private void CopyOver(string appDir, string exeName)
    {
        var exe = Path.Combine(_stagingFolder, exeName);
        var files = Directory.EnumerateFiles(_stagingFolder, "*", SearchOption.AllDirectories)
            .Where(f => !SelfUpdater.SamePath(f, exe))
            .Append(exe);
        foreach (var file in files)
        {
            var destination = Path.Combine(appDir, Path.GetRelativePath(_stagingFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyWithRetries(file, destination);
        }
    }

    // The old process has exited, but the OS (or a virus scanner) may hold its files for a moment.
    private void CopyWithRetries(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 40)
            {
                Thread.Sleep(_retryDelay);
            }
        }
    }

    private Result Abandon((string AppDir, string ExeName)? target, Result result, string error)
    {
        _logger.LogError("Launcher update abandoned: {Error}", error);
        if (target is not { } t)
        {
            return result;
        }

        try
        {
            UpdateResultMarker.RecordError(MarkerPath(t.AppDir), error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "Could not record the error in the self-update marker");
        }

        Restart(t.AppDir, t.ExeName);
        return result;
    }

    private void Restart(string appDir, string exeName)
    {
        var exe = Path.Combine(appDir, exeName);
        try
        {
            if (!OperatingSystem.IsWindows() && File.Exists(exe))
            {
                File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            _start(exe, appDir);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(e, "Could not start {Exe}", exe);
        }
    }

    // appDir and exeName when they are usable: an existing, fully qualified folder and a plain file name in it.
    private static (string AppDir, string ExeName)? ParseTarget(IReadOnlyList<string> args)
    {
        if (args.Count < 5)
        {
            return null;
        }

        var appDir = args[3];
        var exeName = args[4];
        if (!Path.IsPathFullyQualified(appDir) || !Directory.Exists(appDir)
            || exeName.Length == 0 || Path.GetFileName(exeName) != exeName || exeName is "." or ".."
            || exeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        return (Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)), exeName);
    }

    internal static string MarkerPath(string appDir) =>
        Path.Combine(appDir, LauncherDataFolder.PortableFolderName, LauncherDataFolder.UpdateResultFileName);

    private static bool WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true; // Already gone
        }
    }

    private static void StartProcess(string exe, string workingFolder)
    {
        using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = workingFolder });
    }
}
