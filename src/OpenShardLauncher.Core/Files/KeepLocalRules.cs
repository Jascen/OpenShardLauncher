using System.IO.Enumeration;

namespace OpenShardLauncher.Core.Files;

// LauncherOptions.KeepLocalPatterns: simple globs (e.g. "*.cfg") matched against the whole feed name, case ignored.
// A matching file is downloaded only when missing, never replaced and never removed.
public sealed class KeepLocalRules(IEnumerable<string> patterns)
{
    private readonly string[] _patterns = [.. patterns.Where(p => !string.IsNullOrWhiteSpace(p))];

    public bool Matches(string name) =>
        _patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));
}
