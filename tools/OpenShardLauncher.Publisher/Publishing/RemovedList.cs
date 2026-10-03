using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Publisher.Publishing;

// The signed "removed" list: names an earlier publish had and a later one dropped, so launchers can delete exactly
// those (never "anything not in the list"; players keep their own files in the install folder).
internal static class RemovedList
{
    public static IReadOnlyList<RemovedEntry> Next(
        FileList? previous, IReadOnlyCollection<FileEntry> current, long version, DateTimeOffset? forgetBefore)
    {
        // Case-insensitive: a name that only changed case is the same file on Windows and macOS, and deleting the
        // old spelling there would delete the new file
        var currentNames = current.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = new Dictionary<string, RemovedEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in previous?.Removed ?? [])
        {
            removed[entry.Name] = entry;
        }

        foreach (var file in previous?.Files ?? [])
        {
            if (!currentNames.Contains(file.Name))
            {
                removed[file.Name] = new RemovedEntry(file.Name, version);
            }
        }

        var cutoff = forgetBefore?.ToUnixTimeSeconds();
        return removed.Values
            .Where(e => !currentNames.Contains(e.Name))
            .Where(e => cutoff is null || e.RemovedIn >= cutoff)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }
}
