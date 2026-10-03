using System.Diagnostics.CodeAnalysis;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Core.Storage;

// The effective update server: the player's override, else LauncherOptions.UpdateUrl. Always ends with '/', so feed
// paths can be combined with it as relative URIs.
public sealed class ServerEndpoint
{
    private readonly Lock _lock = new();
    private Uri? _override;

    public ServerEndpoint(LauncherOptions options, string? serverUrlOverride)
    {
        if (!TryNormalize(options.UpdateUrl, out var defaultUrl))
        {
            throw new ArgumentException($"UpdateUrl '{options.UpdateUrl}' in launcher.json is not a valid http(s) address.", nameof(options));
        }

        Default = defaultUrl;
        SetOverride(serverUrlOverride, raiseChanged: false);
    }

    // Raised after Current changes. Subscribers restart the install session.
    public event EventHandler? Changed;

    public Uri Default { get; }

    public Uri Current
    {
        get
        {
            lock (_lock)
            {
                return _override ?? Default;
            }
        }
    }

    public bool IsDefault => Current == Default;

    // What to store as UserSettings.ServerUrlOverride: null when it is the default.
    public string? Override
    {
        get
        {
            lock (_lock)
            {
                return _override?.AbsoluteUri;
            }
        }
    }

    // Null, blank or the default clears the override. An invalid address is refused (the settings dialog validates
    // first); a stored one that has become invalid falls back to the default.
    public void SetOverride(string? url) => SetOverride(url, raiseChanged: true);

    // Absolute http/https with a host, no user info, query or fragment; a trailing '/' is added to the path.
    public static bool TryNormalize(string? url, [NotNullWhen(true)] out Uri? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return false;
        }

        normalized = Normalize(uri);
        return true;
    }

    public static Uri Normalize(Uri uri)
    {
        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private void SetOverride(string? url, bool raiseChanged)
    {
        Uri? value = null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!TryNormalize(url, out var normalized))
            {
                if (raiseChanged)
                {
                    throw new ArgumentException($"'{url}' is not a valid http(s) server address.", nameof(url));
                }
            }
            else if (normalized != Default)
            {
                value = normalized;
            }
        }

        bool changed;
        lock (_lock)
        {
            changed = _override != value;
            _override = value;
        }

        if (changed && raiseChanged)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
