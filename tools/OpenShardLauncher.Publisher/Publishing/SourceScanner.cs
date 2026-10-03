using System.Text;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Publisher.Publishing;

internal sealed record SourceFile(string Name, string FullPath, long Size, DateTime LastWriteUtc);

// Lists the game files to publish: everything under --source except the default exclusions and .publishignore.
// Refuses the whole source when it holds a private key or a name the launcher couldn't place safely.
internal static class SourceScanner
{
    // Secrets and tooling that never belong in a feed, plus the launcher's reserved names in the install folder.
    // A .publishignore line starting with ! can bring a file back (e.g. !Data/appsettings.json).
    public const string DefaultExclusions = """
        .git/
        .env*
        *.pem
        *.key
        appsettings*.json
        /.publishignore
        /.openshardignore
        /.openshardlauncher-cache/
        """;

    // Private keys are small; bigger files aren't read for the check
    private const long KeyScanLimit = 64 * 1024;

    public static IReadOnlyList<SourceFile> Scan(string sourceRoot)
    {
        var publishIgnorePath = Path.Combine(sourceRoot, IgnoreRules.PublishFileName);
        var rules = IgnoreRules.Combine(
            IgnoreRules.Parse(DefaultExclusions),
            IgnoreRules.Parse(File.Exists(publishIgnorePath) ? File.ReadAllText(publishIgnorePath) : null));

        // Symlinks are skipped rather than followed, so nothing outside --source can be published by accident
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };

        var files = new List<SourceFile>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in new DirectoryInfo(sourceRoot).EnumerateFiles("*", enumeration))
        {
            var relative = Path.GetRelativePath(sourceRoot, info.FullName);
            if (Path.DirectorySeparatorChar != '\\' && relative.Contains('\\'))
            {
                throw new PublishException($"'{relative}' has a backslash in its name, which Windows can't store. Rename it.");
            }

            var name = relative.Replace(Path.DirectorySeparatorChar, '/');
            if (!name.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) && IsPrivateKey(info))
            {
                throw new PublishException(
                    $"'{name}' is a private key. Move it out of --source; keys must never be near files that get uploaded.");
            }

            if (rules.IsIgnored(name))
            {
                continue;
            }

            if (!PathContainment.TryResolve(sourceRoot, name, out _))
            {
                throw new PublishException($"'{name}' would resolve outside the install folder.");
            }

            if (!names.TryAdd(name, name))
            {
                throw new PublishException(
                    $"'{names[name]}' and '{name}' differ only by case and would be one file on Windows and macOS.");
            }

            files.Add(new SourceFile(name, info.FullName, info.Length, info.LastWriteTimeUtc));
        }

        files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return files;
    }

    private static bool IsPrivateKey(FileInfo file)
    {
        if (file.Length > KeyScanLimit)
        {
            return false;
        }

        // Covers PKCS#8 ("PRIVATE KEY", "ENCRYPTED PRIVATE KEY") and the older "EC/RSA PRIVATE KEY" labels
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(file.FullName));
        return text.Contains("-----BEGIN ", StringComparison.Ordinal)
            && text.Contains("PRIVATE KEY-----", StringComparison.Ordinal);
    }
}
