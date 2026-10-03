using System.Globalization;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Client.Presentation;

// UpdateProgress/FileProgress → the text above the progress bars.
public static class ProgressTextFormatter
{
    public static LocalizedText Format(UpdateProgress progress) => progress.Phase switch
    {
        UpdatePhase.FetchingFileList => new(StringKeys.FetchingFileList),
        UpdatePhase.Comparing => new(StringKeys.ComparingFiles, progress.FilesDone, progress.FilesTotal),
        UpdatePhase.Downloading when progress.BytesTotal > 0 => new(StringKeys.DownloadingBytes,
            Units.Bytes(progress.BytesDone), Units.Bytes(progress.BytesTotal), Units.Speed(progress.BytesPerSecond),
            progress.TimeLeft is { } left ? Units.Duration(left) : "?"),
        UpdatePhase.Downloading => new(StringKeys.DownloadingFiles,
            progress.FilesDone, progress.FilesTotal, Units.Speed(progress.BytesPerSecond)),
        UpdatePhase.RemovingFiles => new(StringKeys.RemovingFiles, progress.FilesDone, progress.FilesTotal),
        UpdatePhase.InstallingTazUO when progress.BytesTotal > 0 =>
            new(StringKeys.DownloadingTazUO, Units.Bytes(progress.BytesDone), Units.Bytes(progress.BytesTotal)),
        UpdatePhase.InstallingTazUO => new(StringKeys.InstallingTazUO),
        UpdatePhase.UpdatingLauncher when progress.BytesTotal > 0 =>
            new(StringKeys.DownloadingLauncher, Units.Bytes(progress.BytesDone), Units.Bytes(progress.BytesTotal)),
        UpdatePhase.UpdatingLauncher => new(StringKeys.UpdatingLauncher),
        _ => throw new ArgumentOutOfRangeException(nameof(progress), progress.Phase, null),
    };

    public static LocalizedText Format(FileProgress file) => new(StringKeys.CurrentFile, file.Name);
}

// Human-readable sizes, speeds and durations.
public static class Units
{
    public static string Bytes(double bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => Scaled(bytes / (1024 * 1024 * 1024), "GB"),
        >= 1024 * 1024 => Scaled(bytes / (1024 * 1024), "MB"),
        >= 1024 => Scaled(bytes / 1024, "KB"),
        _ => bytes.ToString("F0", CultureInfo.CurrentCulture) + " B",
    };

    public static string Speed(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    // Rounded up to whole seconds: "45s", "3m 20s", "1h 5m".
    public static string Duration(TimeSpan time)
    {
        var seconds = (long)Math.Ceiling(Math.Max(0, time.TotalSeconds));
        return seconds switch
        {
            < 60 => $"{seconds}s",
            < 3600 => $"{seconds / 60}m {seconds % 60}s",
            _ => $"{seconds / 3600}h {seconds % 3600 / 60}m",
        };
    }

    private static string Scaled(double value, string unit) =>
        value.ToString(value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + unit;
}
