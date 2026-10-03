using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Files;

public sealed record PlannedRemoval(string Name, string FullPath);

// Turns the verified "removed" list into files that are safe to delete. Files are only ever deleted through this list,
// never because they're missing from files.json: players keep their own files in the install folder.
public sealed class RemovalPlanner(KeepLocalRules keepLocal)
{
    // Skips a name that is still published, resolves outside the install folder, is reserved, is keep-local or matches
    // the player's ignore list. Whether the file exists is checked when deleting.
    public IReadOnlyList<PlannedRemoval> Plan(FileList list, InstallFolder folder, IgnoreRules ignoreRules)
    {
        var published = new HashSet<string>(list.Files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        var planned = new List<PlannedRemoval>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var removed in list.Removed)
        {
            var name = removed.Name;
            if (published.Contains(name)
                || keepLocal.Matches(name)
                || ignoreRules.Match(name) is not null
                || !folder.TryGetPathFor(name, out var fullPath)
                || !seen.Add(fullPath))
            {
                continue;
            }

            planned.Add(new PlannedRemoval(name, fullPath));
        }

        return planned;
    }
}
