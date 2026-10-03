using System.Reflection;

namespace OpenShardLauncher.Infrastructure.Platform;

// The running launcher's version, from AssemblyInformationalVersion (the <Version> the release build sets), without
// the "+<commit>" suffix the SDK appends. Compared with package versions to offer a self-update.
public static class LauncherVersion
{
    public static string Current { get; } = From(Assembly.GetEntryAssembly() ?? typeof(LauncherVersion).Assembly);

    // Null when it isn't a plain numeric version (e.g. "1.2.0-dev"); such a build is never offered a self-update
    // that it can't compare against.
    public static Version? CurrentVersion { get; } = Version.TryParse(Current, out var version) ? version : null;

    internal static string From(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
