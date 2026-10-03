using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenShardLauncher.Core.Storage;

// Each local file's SHA-256 with the size and modified time it had when hashed, so unchanged files aren't re-hashed
// on every check. Lives in the install folder's cache folder, so it moves with the game files. Thread-safe.
public sealed class HashCache
{
    public const string FileName = "hashes.json";

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private int _changed;

    private HashCache(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
    }

    // A missing or damaged cache only costs a full re-hash.
    public static HashCache Load(InstallFolder folder, ILogger logger)
    {
        var cache = new HashCache(Path.Combine(folder.CacheFolder, FileName), logger);
        try
        {
            if (File.Exists(cache._path))
            {
                var entries = JsonSerializer.Deserialize(File.ReadAllText(cache._path), CoreJsonContext.Default.DictionaryStringEntry);
                foreach (var (name, entry) in entries ?? [])
                {
                    cache._entries[name] = entry;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(e, "Ignoring damaged hash cache {Path}", cache._path);
        }

        return cache;
    }

    public bool TryGet(string name, long size, DateTime modifiedUtc, out string sha256)
    {
        if (_entries.TryGetValue(name, out var entry) && entry.Size == size && entry.Modified == modifiedUtc.Ticks)
        {
            sha256 = entry.Sha256;
            return true;
        }

        sha256 = "";
        return false;
    }

    public void Set(string name, long size, DateTime modifiedUtc, string sha256)
    {
        _entries[name] = new Entry(size, modifiedUtc.Ticks, sha256);
        Volatile.Write(ref _changed, 1);
    }

    public void Remove(string name)
    {
        if (_entries.TryRemove(name, out _))
        {
            Volatile.Write(ref _changed, 1);
        }
    }

    // Saves if anything changed. Failing to save only costs a re-hash next time.
    public void Save()
    {
        if (Interlocked.Exchange(ref _changed, 0) == 0)
        {
            return;
        }

        try
        {
            var snapshot = new Dictionary<string, Entry>(_entries, StringComparer.Ordinal);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(snapshot, CoreJsonContext.Default.DictionaryStringEntry));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not save hash cache {Path}", _path);
        }
    }

    public sealed record Entry(long Size, long Modified, string Sha256);
}
