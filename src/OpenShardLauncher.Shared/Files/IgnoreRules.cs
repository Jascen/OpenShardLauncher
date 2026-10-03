using System.IO.Enumeration;

namespace OpenShardLauncher.Shared.Files;

// A list of files and folders to leave alone, read like a .gitignore but with globs only (no regex, no [sets]).
// Used for the player's ignore list (.openshardignore in the install folder) and the Publisher's .publishignore.
// The syntax is documented in docs/ignore-rules.md:
//   - one pattern per line; blank lines and lines starting with # are skipped; surrounding spaces are trimmed
//   - / separates folders (\ is accepted too); * and ? match within one name; a ** segment matches any number of folders
//   - a trailing / matches folders only
//   - a leading / or any / in the middle anchors the pattern to the top folder; otherwise it matches a name at any depth
//   - ! brings back something an earlier line ignored; the last matching line wins. As in git, a file inside an
//     ignored folder can't be brought back
//   - \# and \! at the start stand for a name that really starts with # or !
//   - matching ignores case
public sealed class IgnoreRules
{
    public const string PlayerFileName = ".openshardignore";
    public const string PublishFileName = ".publishignore";

    public static readonly IgnoreRules None = new([]);

    private readonly IReadOnlyList<Rule> _rules;

    private IgnoreRules(IReadOnlyList<Rule> rules) => _rules = rules;

    public bool IsEmpty => _rules.Count == 0;

    public static IgnoreRules Parse(string? text)
    {
        var rules = new List<Rule>();
        foreach (var rawLine in (text ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var negated = line.StartsWith('!');
            if (negated)
            {
                line = line[1..];
            }

            if (line.StartsWith(@"\#", StringComparison.Ordinal) || line.StartsWith(@"\!", StringComparison.Ordinal))
            {
                line = line[1..];
            }

            line = line.Replace('\\', '/');
            var foldersOnly = line.EndsWith('/');
            line = line.TrimEnd('/');

            // A slash anywhere but the end ties the pattern to the top folder
            var anchored = line.Contains('/');
            line = line.TrimStart('/');
            if (line.Length == 0)
            {
                continue;
            }

            rules.Add(new Rule(line.Split('/', StringSplitOptions.RemoveEmptyEntries), anchored, negated, foldersOnly));
        }

        return new IgnoreRules(rules);
    }

    // Later rules win over earlier ones, so a .publishignore can bring back a default exclusion with !
    public static IgnoreRules Combine(IgnoreRules first, IgnoreRules second) =>
        new([.. first._rules, .. second._rules]);

    public bool IsIgnored(string name) => Match(name) is not null;

    // What ignores a name (folders separated by /): the topmost ignored folder it is in, with a trailing / (e.g.
    // "Music/"), or the name itself when only the file is ignored. Null when it isn't ignored.
    public string? Match(string name)
    {
        if (IsEmpty)
        {
            return null;
        }

        var segments = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var length = 1; length < segments.Length; length++)
        {
            if (IsIgnored(segments.AsSpan(0, length), isFolder: true))
            {
                return string.Join('/', segments, 0, length) + "/";
            }
        }

        return segments.Length > 0 && IsIgnored(segments, isFolder: false) ? name : null;
    }

    private bool IsIgnored(ReadOnlySpan<string> path, bool isFolder)
    {
        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.FoldersOnly && !isFolder)
            {
                continue;
            }

            // An unanchored pattern has a single segment and is compared with the last name only. Every parent folder
            // is tested on its own (see Match), which is what lets it match at any depth.
            var matches = rule.Anchored
                ? SegmentsMatch(rule.Segments, path)
                : SegmentsMatch(rule.Segments, path[^1..]);
            if (matches)
            {
                ignored = !rule.Negated;
            }
        }

        return ignored;
    }

    private static bool SegmentsMatch(ReadOnlySpan<string> pattern, ReadOnlySpan<string> path)
    {
        if (pattern.IsEmpty)
        {
            return path.IsEmpty;
        }

        if (pattern[0] == "**")
        {
            // Any number of folders, including none
            for (var skip = 0; skip <= path.Length; skip++)
            {
                if (SegmentsMatch(pattern[1..], path[skip..]))
                {
                    return true;
                }
            }

            return false;
        }

        return !path.IsEmpty
            && FileSystemName.MatchesSimpleExpression(pattern[0], path[0], ignoreCase: true)
            && SegmentsMatch(pattern[1..], path[1..]);
    }

    private sealed record Rule(string[] Segments, bool Anchored, bool Negated, bool FoldersOnly);
}
