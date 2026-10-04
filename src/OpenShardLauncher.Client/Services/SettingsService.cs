using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Http;

namespace OpenShardLauncher.Client.Services;

// The player's settings for this run, loaded once from settings.json, and the two services that mirror them for the
// HTTP layer: the effective server (ServerEndpoint) and "Allow insecure downloads" (TransportPolicy). Both are created
// here so they match the settings from the first request, and Save keeps them in step.
public sealed class SettingsService
{
    private readonly SettingsStore _store;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(SettingsStore store, LauncherOptions options, ILogger<SettingsService> logger)
    {
        _store = store;
        _logger = logger;
        Current = store.Load();
        ServerEndpoint = new ServerEndpoint(options, Current.ServerUrlOverride);
        TransportPolicy = new TransportPolicy { AllowInsecureDownloads = Current.AllowInsecureDownloads };
    }

    public UserSettings Current { get; private set; }

    public ServerEndpoint ServerEndpoint { get; }

    public TransportPolicy TransportPolicy { get; }

    // Applies the settings straight away: the next request uses the new server and transport rule, without a restart.
    // A changed server raises ServerEndpoint.Changed. If settings.json can't be written they still apply to this run.
    public void Save(UserSettings settings)
    {
        Current = settings;
        try
        {
            _store.Save(settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "Could not save the settings; they apply until the launcher closes");
        }

        TransportPolicy.AllowInsecureDownloads = settings.AllowInsecureDownloads;
        ServerEndpoint.SetOverride(settings.ServerUrlOverride);
    }
}
