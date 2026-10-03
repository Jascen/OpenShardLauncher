using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenShardLauncher.Core.Model;
using Polly;
using Polly.Retry;

namespace OpenShardLauncher.Infrastructure.Http;

// The Polly pipelines that retry whole feed operations: a download including its hash check and final move, or a
// document fetch. They sit outside the HttpClient, so every attempt passes SecureTransportHandler again.
public static class RetryPipelines
{
    public const string Files = "OpenShardLauncher.Files";
    public const string Packages = "OpenShardLauncher.Packages";

    internal static IServiceCollection AddRetryPipelines(this IServiceCollection services)
    {
        services.AddResiliencePipeline(Files, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<DownloadOptions>>().Value;
            builder.AddRetry(RetryOptions(options.FileAttempts, options.RetryBaseDelay));
        });
        services.AddResiliencePipeline(Packages, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<DownloadOptions>>().Value;
            builder.AddRetry(RetryOptions(options.PackageAttempts, options.RetryBaseDelay));
        });
        return services;
    }

    // Transient: network errors, timeouts, a stalled or cut-off body and a download whose hash or size came out
    // wrong. Never retried: UpdateException (a locked file, a full disk, a refused address, a missing blob), which
    // retrying can't fix, and cancellation.
    internal static bool IsTransient(Exception e) => e switch
    {
        UpdateException => false,
        OperationCanceledException { InnerException: TimeoutException } => true, // HttpClient.Timeout
        OperationCanceledException => false,
        HttpRequestException or IOException or TimeoutException or InvalidDataException => true,
        _ => false,
    };

    private static RetryStrategyOptions RetryOptions(int attempts, TimeSpan baseDelay) => new()
    {
        MaxRetryAttempts = Math.Max(0, attempts - 1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = false,
        Delay = baseDelay,
        ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransient),
    };
}
