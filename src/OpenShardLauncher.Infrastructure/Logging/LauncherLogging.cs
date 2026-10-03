using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace OpenShardLauncher.Infrastructure.Logging;

// Serilog as the ILogger provider for the launcher: a daily rolling file in the launcher data folder's logs/, kept for
// a week. Shared mode, so two launchers started from the same folder don't fight over the file.
public static class LauncherLogging
{
    public const string FileNamePrefix = "launcher-";

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static ILoggingBuilder AddLauncherFileLogging(this ILoggingBuilder logging, string logsFolder, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            // One line per HTTP request is noise; retries (Polly) and failures still come through as warnings.
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logsFolder, FileNamePrefix + ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: OutputTemplate,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        return logging.AddSerilog(logger, dispose: true);
    }
}
