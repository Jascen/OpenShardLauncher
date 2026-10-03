using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Files;

// One feed file to compare with the install folder. KeepLocal files only count as different when missing.
public sealed record FileToCompare(FileEntry Entry, bool KeepLocal)
{
    public string Name => Entry.Name;
}

public sealed record FilterResult(IReadOnlyList<FileToCompare> ToCompare, IReadOnlyList<string> IgnoredItems);

// Decides which feed files are compared: reserved names and names resolving outside the install folder are dropped
// silently (the feed may never write them), names matching the player's ignore list are dropped and reported,
// keep-local names are marked.
public sealed class FileListFilter(KeepLocalRules keepLocal)
{
    public FilterResult Apply(IReadOnlyList<FileEntry> files, InstallFolder folder, IgnoreRules ignoreRules)
    {
        var toCompare = new List<FileToCompare>(files.Count);
        var ignored = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (!folder.TryGetPathFor(file.Name, out _))
            {
                continue;
            }

            // The topmost ignored folder ("Music/") or the name, so a folder is listed once.
            if (ignoreRules.Match(file.Name) is { } match)
            {
                ignored.Add(match);
                continue;
            }

            toCompare.Add(new FileToCompare(file, keepLocal.Matches(file.Name)));
        }

        return new FilterResult(toCompare, [.. ignored]);
    }
}
