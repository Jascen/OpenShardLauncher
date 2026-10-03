using System.Net;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Infrastructure.Http;

// Enforces TransportPolicy on every request, and follows redirects itself (the primary handler has automatic redirects
// off) so every hop is checked: a redirect may not go from https to http, or to an address the policy refuses.
// Refusals throw UpdateException(InsecureServer), which the retry pipelines never retry.
//
// The retry pipelines wrap whole operations from outside the HttpClient, so every attempt passes through here.
public sealed class SecureTransportHandler(TransportPolicy policy) : DelegatingHandler
{
    public const int MaxRedirects = 5;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Check(request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        for (var hops = 0; IsRedirect(response.StatusCode) && response.Headers.Location is { } location; hops++)
        {
            var from = request.RequestUri!;
            response.Dispose();

            if (hops == MaxRedirects)
            {
                throw new HttpRequestException($"More than {MaxRedirects} redirects from {from}.");
            }

            var target = location.IsAbsoluteUri ? location : new Uri(from, location);
            if (from.Scheme == Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttps)
            {
                throw new UpdateException(UpdateError.InsecureServer, inner: new HttpRequestException(
                    $"Refused a redirect from {from} to {target}: it would leave https."));
            }

            Check(target);
            request = Redirect(request, target, response.StatusCode);
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private void Check(Uri? uri)
    {
        if (uri is null || !policy.IsAllowed(uri))
        {
            throw new UpdateException(UpdateError.InsecureServer, inner: new HttpRequestException(
                $"Refused {uri}: only https, or http to this machine, is allowed unless insecure downloads are on."));
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    // The launcher only sends GET (and HEAD), which have no content, so a redirect is the same request at a new
    // address. Headers such as Range carry over.
    private static HttpRequestMessage Redirect(HttpRequestMessage original, Uri target, HttpStatusCode status)
    {
        var method = status == HttpStatusCode.SeeOther && original.Method != HttpMethod.Head ? HttpMethod.Get : original.Method;
        var next = new HttpRequestMessage(method, target)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };

        foreach (var (name, values) in original.Headers)
        {
            next.Headers.TryAddWithoutValidation(name, values);
        }

        return next;
    }
}
