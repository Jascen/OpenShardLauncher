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
    public static FileList Parse(ReadOnlySpan<byte> json)
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

        list.Validate();
        return list;
    }

    private void Validate()
    {
        if (Version <= 0)
        {
            throw new InvalidDataException($"{FeedLayout.FileListPath} has no version.");
        }

        // Case-insensitive, because two names differing only by case are one file on Windows and macOS
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            if (string.IsNullOrEmpty(file.Name) || !Sha256Hex.IsValid(file.Sha256) || file.Size < 0)
            {
                throw new InvalidDataException($"{FeedLayout.FileListPath} has an invalid entry '{file.Name}'.");
            }

            if (!names.Add(file.Name))
            {
                throw new InvalidDataException($"{FeedLayout.FileListPath} lists '{file.Name}' twice.");
            }
        }

        foreach (var removed in Removed)
        {
            if (string.IsNullOrEmpty(removed.Name))
            {
                throw new InvalidDataException($"{FeedLayout.FileListPath} has a removed entry without a name.");
            }
        }
    }
}
