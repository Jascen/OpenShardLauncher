using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenShardLauncher.Publisher.Publishing;

// SHA-256 of source files by full path, size and last-write time, so a publish only re-hashes what changed. It is the
// only thing in the state folder: losing it just means hashing everything again.
internal sealed class HashCache
{
    public const string FileName = "hash-cache.json";

    private readonly Dictionary<string, HashCacheEntry> _previous;
    private readonly ConcurrentDictionary<string, HashCacheEntry> _current = new(StringComparer.Ordinal);

    private HashCache(Dictionary<string, HashCacheEntry> previous) => _previous = previous;

    // A missing or unreadable cache is treated as empty
    public static HashCache Load(string stateDirectory)
    {
        var path = Path.Combine(stateDirectory, FileName);
        try
        {
            using var stream = File.OpenRead(path);
            var entries = JsonSerializer.Deserialize(stream, HashCacheJsonContext.Default.DictionaryStringHashCacheEntry);
            return new HashCache(new Dictionary<string, HashCacheEntry>(entries ?? [], StringComparer.Ordinal));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new HashCache(new Dictionary<string, HashCacheEntry>(StringComparer.Ordinal));
        }
    }

    public string? Find(string fullPath, long size, DateTime lastWriteUtc)
    {
        if (_previous.TryGetValue(fullPath, out var entry) && entry.Size == size && entry.LastWriteUtcTicks == lastWriteUtc.Ticks)
        {
            _current[fullPath] = entry;
            return entry.Sha256;
        }

        return null;
    }

    public void Add(string fullPath, long size, DateTime lastWriteUtc, string sha256) =>
        _current[fullPath] = new HashCacheEntry(size, lastWriteUtc.Ticks, sha256);

    // Keeps only the files seen in this run, so the cache doesn't grow forever
    public void Save(string stateDirectory)
    {
        Directory.CreateDirectory(stateDirectory);
        var sorted = new SortedDictionary<string, HashCacheEntry>(_current, StringComparer.Ordinal);
        AtomicFile.WriteAllBytes(
            Path.Combine(stateDirectory, FileName),
            JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, HashCacheEntry>(sorted), HashCacheJsonContext.Default.DictionaryStringHashCacheEntry));
    }
}

internal sealed record HashCacheEntry(long Size, long LastWriteUtcTicks, string Sha256);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Dictionary<string, HashCacheEntry>))]
internal sealed partial class HashCacheJsonContext : JsonSerializerContext;
