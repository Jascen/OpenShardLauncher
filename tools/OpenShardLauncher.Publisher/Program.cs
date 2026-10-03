using System.Security.Cryptography;
using OpenShardLauncher.Publisher.Cli;
using OpenShardLauncher.Publisher.Publishing;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

// Run on the operator's own machine. The private key never goes on the server, in CI or in the repository.
const string Usage = """
    Usage:
      publisher publish --source <game files> --packages <zips> --out <feed> --key <key> [--key <key2>]
                        [--state <dir>] [--forget-removed-before <date>]
      publisher publish --source <game files> --packages <zips> --out <feed> --unsigned
      publisher keygen [--out <dir>]
      publisher verify --feed <feed> [--pub <key or file>]...

    Key passwords come from PUBLISHER_KEY_PASSWORD, or are asked for.
    """;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "{Level:u3} {Message:lj}{NewLine}{Exception}",
        standardErrorFromLevel: LogEventLevel.Warning)
    .CreateLogger();
using var loggers = new SerilogLoggerFactory(Log.Logger, dispose: true);

try
{
    var rest = args.Skip(1).ToArray();
    return args.FirstOrDefault() switch
    {
        "publish" => Commands.Publish(rest, loggers),
        "keygen" => Commands.Keygen(rest, loggers),
        "verify" => Commands.Verify(rest, loggers),
        null or "-h" or "--help" or "help" => ShowUsage(ExitCodes.Success),
        var unknown => throw new UsageException($"Unknown command '{unknown}'."),
    };
}
catch (UsageException e)
{
    Log.Error(e.Message);
    return ShowUsage(ExitCodes.Usage);
}
catch (Exception e) when (e is PublishException or IOException or UnauthorizedAccessException
    or CryptographicException or InvalidDataException)
{
    Log.Error(e.Message);
    return ExitCodes.Failed;
}

int ShowUsage(int exitCode)
{
    Console.Error.WriteLine(Usage);
    return exitCode;
}
