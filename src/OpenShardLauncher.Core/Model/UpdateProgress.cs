namespace OpenShardLauncher.Core.Model;

public enum UpdatePhase
{
    FetchingFileList,
    Comparing,
    Downloading,
    RemovingFiles,
    InstallingClient, // Downloading and installing the game client, then setting up its TazUO profiles
    UpdatingLauncher, // Downloading and staging a new version of this launcher
}

// Overall progress of a check or download. FilesDone/FilesTotal count files. The byte fields are only set while
// downloading; BytesTotal is the size of everything queued.
public sealed record UpdateProgress(
    UpdatePhase Phase,
    int FilesDone,
    int FilesTotal,
    long BytesDone = 0,
    long BytesTotal = 0,
    double BytesPerSecond = 0)
{
    public double Percent => BytesTotal > 0 ? Math.Min(100, BytesDone * 100.0 / BytesTotal)
        : FilesTotal > 0 ? FilesDone * 100.0 / FilesTotal : 0;

    // Null until there is a size and a speed to estimate from.
    public TimeSpan? TimeLeft => BytesTotal > 0 && BytesPerSecond > 0
        ? TimeSpan.FromSeconds(Math.Max(0, BytesTotal - BytesDone) / BytesPerSecond)
        : null;
}

// Progress of the single file currently downloading.
public sealed record FileProgress(string Name, long BytesDone, long BytesTotal)
{
    public double Percent => BytesTotal > 0 ? Math.Min(100, BytesDone * 100.0 / BytesTotal) : 0;
}
