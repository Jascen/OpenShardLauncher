using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenShardLauncher.Core.Storage;

// What the launcher has seen from feeds, for rollback and downgrade protection: the last files.json version per
// server, and per package role ("launcher", "tazuo") the installed version and the highest manifest version.
// feed-state.json in the launcher data folder. Thread-safe; every change is saved straight away.
public sealed class FeedStateStore
{
    private readonly string _path;
    private readonly ILogger<FeedStateStore> _logger;
    private readonly Lock _lock = new();
    private readonly FeedState _state;

    public FeedStateStore(LauncherDataFolder dataFolder, ILogger<FeedStateStore> logger)
    {
        _path = dataFolder.FeedStateFile;
        _logger = logger;
        _state = Load();
    }

    public long? GetLastFileListVersion(Uri server)
    {
        lock (_lock)
        {
            return _state.FileListVersions.TryGetValue(Key(server), out var version) ? version : null;
        }
    }

    // Only ever raises the stored version.
    public void SetLastFileListVersion(Uri server, long version)
    {
        lock (_lock)
        {
            var key = Key(server);
            if (_state.FileListVersions.TryGetValue(key, out var existing) && existing >= version)
            {
                return;
            }

            _state.FileListVersions[key] = version;
            Save();
        }
    }

    public string? GetInstalledVersion(string role)
    {
        lock (_lock)
        {
            return _state.Packages.TryGetValue(role, out var package) ? package.InstalledVersion : null;
        }
    }

    public void SetInstalledVersion(string role, string version) =>
        Update(role, package => package with { InstalledVersion = version });

    public string? GetHighestManifestVersion(string role)
    {
        lock (_lock)
        {
            return _state.Packages.TryGetValue(role, out var package) ? package.HighestManifestVersion : null;
        }
    }

    // The caller compares versions (their format belongs to the manifest) and only calls this with a higher one.
    public void SetHighestManifestVersion(string role, string version) =>
        Update(role, package => package with { HighestManifestVersion = version });

    private void Update(string role, Func<PackageState, PackageState> change)
    {
        lock (_lock)
        {
            _state.Packages[role] = change(_state.Packages.GetValueOrDefault(role) ?? new PackageState());
            Save();
        }
    }

    // Normalized, so "https://host" and "https://HOST/" are the same server.
    private static string Key(Uri server) => ServerEndpoint.Normalize(server).AbsoluteUri;

    private FeedState Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(_path), CoreJsonContext.Default.FeedState) ?? new FeedState();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Losing it weakens rollback protection until the next run, so say so loudly.
            _logger.LogError(e, "Could not read feed state {Path}; starting from empty", _path);
        }

        return new FeedState();
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_state, CoreJsonContext.Default.FeedState));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "Could not save feed state {Path}", _path);
        }
    }
}

internal sealed class FeedState
{
    public Dictionary<string, long> FileListVersions { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, PackageState> Packages { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record PackageState
{
    public string? InstalledVersion { get; init; }

    public string? HighestManifestVersion { get; init; }
}
