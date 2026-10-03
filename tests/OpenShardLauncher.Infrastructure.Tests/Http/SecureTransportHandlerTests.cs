using System.Net;
using System.Net.Http.Headers;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Infrastructure.Http;

namespace OpenShardLauncher.Infrastructure.Tests.Http;

public sealed class SecureTransportHandlerTests
{
    private readonly TransportPolicy _policy = new();
    private readonly StubHandler _network = new();

    [Theory]
    [InlineData("https://updates.example.com/files.json", false, true)]
    [InlineData("http://127.0.0.1:8080/files.json", false, true)]
    [InlineData("http://localhost/files.json", false, true)]
    [InlineData("http://updates.example.com/files.json", false, false)]
    [InlineData("http://updates.example.com/files.json", true, true)]
    public async Task Request_OnlyHttpsLoopbackOrAllowedInsecure_IsSent(string url, bool allowInsecure, bool sent)
    {
        _policy.AllowInsecureDownloads = allowInsecure;

        if (sent)
        {
            using var response = await SendAsync(new Uri(url));
            Assert.Single(_network.Requests);
        }
        else
        {
            var e = await Assert.ThrowsAsync<UpdateException>(() => SendAsync(new Uri(url)));
            Assert.Equal(UpdateError.InsecureServer, e.Error);
            Assert.Empty(_network.Requests);
        }
    }

    [Fact]
    public async Task Redirect_FromHttpsToHttp_IsBlockedEvenWithInsecureAllowed()
    {
        _policy.AllowInsecureDownloads = true;
        _network.Redirect("https://updates.example.com/files.json", "http://mirror.example.com/files.json");

        var e = await Assert.ThrowsAsync<UpdateException>(() => SendAsync(new Uri("https://updates.example.com/files.json")));

        Assert.Equal(UpdateError.InsecureServer, e.Error);
        Assert.Single(_network.Requests);
    }

    [Fact]
    public async Task Redirect_ToRefusedAddress_IsBlocked()
    {
        _network.Redirect("http://127.0.0.1/files.json", "http://updates.example.com/files.json");

        var e = await Assert.ThrowsAsync<UpdateException>(() => SendAsync(new Uri("http://127.0.0.1/files.json")));

        Assert.Equal(UpdateError.InsecureServer, e.Error);
        Assert.Single(_network.Requests);
    }

    [Fact]
    public async Task Redirect_ToHttps_IsFollowedWithTheSameRange()
    {
        _network.Redirect("https://updates.example.com/blobs/ab/abc", "/cdn/ab/abc");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://updates.example.com/blobs/ab/abc");
        request.Headers.Range = new RangeHeaderValue(100, null);
        using var response = await Invoker().SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var followed = _network.Requests[1];
        Assert.Equal("https://updates.example.com/cdn/ab/abc", followed.RequestUri!.AbsoluteUri);
        Assert.Equal(100, followed.Headers.Range!.Ranges.Single().From);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await Invoker().SendAsync(request, CancellationToken.None);
    }

    private HttpMessageInvoker Invoker() => new(new SecureTransportHandler(_policy) { InnerHandler = _network });

    // Answers 200, or a 302 for addresses given a redirect, and records what it was asked.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _redirects = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Redirect(string from, string to) => _redirects[from] = to;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (_redirects.TryGetValue(request.RequestUri!.AbsoluteUri, out var to))
            {
                response.StatusCode = HttpStatusCode.Found;
                response.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
            }

            return Task.FromResult(response);
        }
    }
}
