using System.Globalization;
using System.Resources;

namespace OpenShardLauncher.Client.Presentation;

// Resolves StringKeys and LocalizedText through Resources/Strings.resx for the current UI culture.
public static class UiText
{
    private static readonly ResourceManager Strings = new("OpenShardLauncher.Client.Resources.Strings", typeof(UiText).Assembly);

    // The key itself when it's missing, so a gap shows up on screen instead of crashing the launcher.
    public static string Get(string key) => Strings.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Get(LocalizedText text) =>
        text.Args.Count == 0 ? Get(text.Key) : string.Format(CultureInfo.CurrentCulture, Get(text.Key), [.. text.Args]);

    internal static bool Exists(string key) => Strings.GetString(key, CultureInfo.InvariantCulture) is not null;
}
