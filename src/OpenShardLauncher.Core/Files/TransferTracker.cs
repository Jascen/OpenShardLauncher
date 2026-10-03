using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Core.Files;

// Download progress shared by all download workers: files and bytes done, speed (bytes received since the start over
// elapsed time) and when the next report is due (at most every 0.5s). Thread-safe.
public sealed class TransferTracker
{
    public static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(0.5);

    private readonly TimeProvider _time;
    private readonly int _totalFiles;
    private readonly long _totalBytes;
    private readonly long _started;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _inFlight = new(StringComparer.Ordinal);
    private int _filesDone;
    private long _finishedBytes;
    private long _received;
    private long _lastReport;
    private bool _reported;

    public TransferTracker(int totalFiles, long totalBytes, TimeProvider time)
    {
        _time = time;
        _totalFiles = totalFiles;
        _totalBytes = totalBytes;
        _started = time.GetTimestamp();
    }

    // A file's bytes on disk so far (including a resumed part), plus how many arrived in this chunk (for the speed).
    // Returns true when a progress report is due.
    public bool OnBytes(string name, long fileBytesDone, long chunkBytes)
    {
        lock (_lock)
        {
            _inFlight[name] = fileBytesDone;
            _received += chunkBytes;
            return TakeReportSlot();
        }
    }

    // Callers report after every finished file regardless of the throttle, so the bars don't lag behind at the end.
    public void OnFileCompleted(string name, long size)
    {
        lock (_lock)
        {
            _inFlight.Remove(name);
            _finishedBytes += size;
            _filesDone++;
        }
    }

    // A failed file stops counting towards bytes done.
    public void OnFileFailed(string name)
    {
        lock (_lock)
        {
            _inFlight.Remove(name);
        }
    }

    public UpdateProgress Snapshot()
    {
        lock (_lock)
        {
            var elapsed = _time.GetElapsedTime(_started).TotalSeconds;
            var done = Math.Min(_totalBytes, _finishedBytes + _inFlight.Values.Sum());
            return new UpdateProgress(
                UpdatePhase.Downloading,
                _filesDone,
                _totalFiles,
                done,
                _totalBytes,
                elapsed > 0 ? _received / elapsed : 0);
        }
    }

    private bool TakeReportSlot()
    {
        var now = _time.GetTimestamp();
        if (_reported && _time.GetElapsedTime(_lastReport, now) < ReportInterval)
        {
            return false;
        }

        _reported = true;
        _lastReport = now;
        return true;
    }
}
