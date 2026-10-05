using System.Text.RegularExpressions;

namespace OpenShardLauncher.Shared.Feed;

// Packages are named {role}-{version}.{rid}.zip, e.g. launcher-1.2.0.win-x64.zip or client-3.4.0.osx-arm64.zip.
// The pattern only allows plain names (no folders), so a manifest entry can never point outside packages/.
public static partial class PackageFileName
{
    [GeneratedRegex(@"^(?<role>launcher|client)-(?<version>\d+(?:\.\d+){1,3})\.(?<rid>[a-z][a-z0-9-]*)\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string fileName, out string role, out Version version, out string rid)
    {
        role = rid = string.Empty;
        version = new Version();

        var match = Pattern().Match(fileName);
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var parsed))
        {
            return false;
        }

        role = match.Groups["role"].Value.ToLowerInvariant();
        version = parsed;
        rid = match.Groups["rid"].Value.ToLowerInvariant();
        return true;
    }

    public static string Format(string role, string version, string rid) => $"{role}-{version}.{rid}.zip";
}
