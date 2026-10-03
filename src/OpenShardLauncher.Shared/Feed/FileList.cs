using System.Text.Json;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Shared.Feed;

// One game file. Name is relative to the install folder with / between folders; Sha256 names its blob.
public sealed record FileEntry(string Name, string Sha256, long Size);

// A name that an earlier publish had and a later one dropped. RemovedIn is the version of the publish that dropped it.
public sealed record RemovedEntry(string Name, long RemovedIn);

// files.json. Version is the UTC Unix time (seconds) of the publish and only ever increases, so a launcher can refuse
// an older list than the last one it saw. Property names are a stable contract; add fields only.
public sealed record FileList(long Version, IReadOnlyList<FileEntry> Files, IReadOnlyList<RemovedEntry> Removed)
{
    // The exact bytes that are signed and written to files.json
    public byte[] ToJsonBytes() => JsonSerializer.SerializeToUtf8Bytes(this, FeedJsonContext.Default.FileList);

    // Only trust the result after the bytes' signature has been checked (or AllowUnsignedFeed applied)
    public static FileList Parse(ReadOnlySpan<byte> json) => Parse(json, onInvalidEntry: null);

    // Like Parse, but an entry with an invalid name, hash or size, or a repeated name, is left out and described to
    // onInvalidEntry instead of failing the whole list. The launcher uses this so one bad entry (a Publisher bug) doesn't
    // stop every other file from updating. The document itself (version, shape) must still be valid.
    public static FileList Parse(ReadOnlySpan<byte> json, Action<string>? onInvalidEntry)
    {
        FileList? list;
        try
        {
            list = JsonSerializer.Deserialize(json, FeedJsonContext.Default.FileList);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{FeedLayout.FileListPath} is not valid: {e.Message}", e);
        }

        if (list is null)
        {
            throw new InvalidDataException($"{FeedLayout.FileListPath} is empty.");
        }

        return list.Validate(onInvalidEntry);
    }

    private FileList Validate(Action<string>? onInvalidEntry)
    {
        if (Version <= 0)
        {
            throw new InvalidDataException($"{FeedLayout.FileListPath} has no version.");
        }

        void Invalid(string problem)
        {
            if (onInvalidEntry is null)
            {
                throw new InvalidDataException($"{FeedLayout.FileListPath} {problem}.");
            }

            onInvalidEntry(problem);
        }

        // Case-insensitive, because two names differing only by case are one file on Windows and macOS
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileEntry>(Files.Count);
        foreach (var file in Files)
        {
            // A null element isn't caught by the serializer's nullable checks
            if (file is null || string.IsNullOrEmpty(file.Name) || !Sha256Hex.IsValid(file.Sha256) || file.Size < 0)
            {
                Invalid($"has an invalid entry '{file?.Name}'");
            }
            else if (!names.Add(file.Name))
            {
                Invalid($"lists '{file.Name}' twice");
            }
            else
            {
                files.Add(file);
            }
        }

        var removed = new List<RemovedEntry>(Removed.Count);
        foreach (var entry in Removed)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Name))
            {
                Invalid("has a removed entry without a name");
            }
            else
            {
                removed.Add(entry);
            }
        }

        return files.Count == Files.Count && removed.Count == Removed.Count ? this : this with { Files = files, Removed = removed };
    }
}
