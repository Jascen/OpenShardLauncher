using System.Diagnostics.CodeAnalysis;

namespace OpenShardLauncher.Shared.Files;

// Where a feed name may land on disk: strictly inside a root folder. It checks where the name *resolves*, not a list
// of known-bad names, so "..", absolute and drive-relative names fail by construction. This is a backstop: the
// security control is the signed files.json. Used by the launcher for every write and delete, and by the Publisher.
public static class PathContainment
{
    // Windows paths are case-insensitive. Elsewhere the root and the result come from the same GetFullPath, so their
    // case already matches and an ordinal check is exact.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // True when name ("Data/art.mul", either separator) resolves to a path strictly below root. The root itself
    // ("." or "a/..") doesn't count, and neither does a sibling that merely shares the root's prefix ("C:\Game2").
    public static bool TryResolve(string root, string name, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;

        // Rooted covers "/etc/passwd", "C:\x", "\x" and drive-relative "C:x"; a NUL would be cut off by the OS
        if (string.IsNullOrEmpty(name) || name.Contains('\0') || Path.IsPathRooted(name))
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(rootFull))
        {
            rootFull += Path.DirectorySeparatorChar;
        }

        var resolved = Path.GetFullPath(rootFull + name);
        if (!resolved.StartsWith(rootFull, PathComparison) || resolved.Length == rootFull.Length)
        {
            return false;
        }

        fullPath = resolved;
        return true;
    }
}
