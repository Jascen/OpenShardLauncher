using System.Net;

namespace OpenShardLauncher.Server.Tests;

public sealed class HealthEndpointTests(FeedServerFactory factory) : IClassFixture<FeedServerFactory>
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
