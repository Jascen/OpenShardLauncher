using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Shared.Feed;

// The manifest plus, per entry, where its zip is now. Ignored lists zips that don't follow the naming convention, and
// Superseded lists older versions of a role and platform, so the Publisher can tell the operator about both.
public sealed record ManifestBuildResult(
    PackageManifest Manifest,
    IReadOnlyDictionary<string, string> SourcePaths,
    IReadOnlyList<string> Ignored,
    IReadOnlyList<string> Superseded);

public static class ManifestBuilder
{
    // Describes the newest package for each role and platform in a folder of zips
    public static ManifestBuildResult Build(string packagesDirectory, DateTimeOffset generated)
    {
        var newest = new Dictionary<(string Role, string Rid), (Version Version, string File)>();
        var ignored = new List<string>();
        var superseded = new List<string>();

        foreach (var path in Directory.EnumerateFiles(packagesDirectory, "*.zip"))
        {
            var name = Path.GetFileName(path);
            if (!PackageFileName.TryParse(name, out var role, out var version, out var rid))
            {
                ignored.Add(name);
                continue;
            }

            var key = (role, rid);
            if (newest.TryGetValue(key, out var current))
            {
                if (version == current.Version)
                {
                    throw new InvalidDataException($"{current.File} and {name} are the same package version.");
                }

                superseded.Add(version > current.Version ? current.File : name);
                if (version < current.Version)
                {
                    continue;
                }
            }

            newest[key] = (version, name);
        }

        var packages = newest
            .OrderBy(p => p.Key.Role, StringComparer.Ordinal).ThenBy(p => p.Key.Rid, StringComparer.Ordinal)
            .Select(p => Describe(packagesDirectory, p.Key.Role, p.Key.Rid, p.Value.Version, p.Value.File))
            .ToList();
        var sourcePaths = packages.ToDictionary(p => p.File, p => Path.Combine(packagesDirectory, p.File), StringComparer.Ordinal);

        ignored.Sort(StringComparer.OrdinalIgnoreCase);
        superseded.Sort(StringComparer.OrdinalIgnoreCase);
        return new ManifestBuildResult(new PackageManifest(generated, packages), sourcePaths, ignored, superseded);
    }

    private static PackageEntry Describe(string directory, string role, string rid, Version version, string file)
    {
        var path = Path.Combine(directory, file);
        return new PackageEntry(role, version.ToString(), rid, file, Sha256Hex.OfFile(path), new FileInfo(path).Length);
    }
}
