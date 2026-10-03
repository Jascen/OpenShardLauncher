namespace OpenShardLauncher.Infrastructure.Http;

// Retry and timeout settings for feed requests. The defaults are what players get; tests set the delays to zero.
public sealed class DownloadOptions
{
    // Exponential: 1, 2, 4, 8s between attempts.
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    // Attempts for a game file (blob), including the first.
    public int FileAttempts { get; set; } = 5;

    // Attempts for a package zip and for the feed documents (files.json, the manifest and their signatures).
    public int PackageAttempts { get; set; } = 3;

    // A download (blob, package or document) that receives nothing for this long is abandoned and retried, resuming
    // from what it has. A slow but steady transfer never times out.
    public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // Time allowed for a response's headers. Bodies are only limited by StallTimeout.
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    // files.json and the manifest are read into memory, so cap them.
    public int MaxDocumentBytes { get; set; } = 64 * 1024 * 1024;

    // When the server answers 429 or 503 without Retry-After, every request to it waits this long.
    public TimeSpan BusyPause { get; set; } = TimeSpan.FromSeconds(5);

    // The longest a Retry-After is honoured for, so a misconfigured server can't stall the launcher for hours.
    public TimeSpan MaxBusyPause { get; set; } = TimeSpan.FromMinutes(2);
}
