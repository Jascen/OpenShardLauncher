namespace OpenShardLauncher.Infrastructure.Http;

// Which addresses the launcher may download from. https always; plain http to this machine (local development);
// plain http to anywhere else only while the player has "Allow insecure downloads" on. With a signed feed plain http
// can't change content (signatures and hashes), it only exposes availability and privacy.
public sealed class TransportPolicy
{
    private volatile bool _allowInsecureDownloads;

    // Mirrors UserSettings.AllowInsecureDownloads; the settings dialog updates it, and it applies to the next request.
    public bool AllowInsecureDownloads
    {
        get => _allowInsecureDownloads;
        set => _allowInsecureDownloads = value;
    }

    public bool IsAllowed(Uri uri) => IsSecure(uri) || (AllowInsecureDownloads && uri.Scheme == Uri.UriSchemeHttp);

    // https, or http to loopback. Unsigned feeds need this whatever the player's setting, because nothing else would
    // prove where their files came from.
    public static bool IsSecure(Uri uri) =>
        uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
