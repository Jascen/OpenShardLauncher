using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Publisher.Publishing;
using OpenShardLauncher.Publisher.Verification;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Publisher.Cli;

internal static class Commands
{
    public const string PrivateKeyFileName = "feed-signing.key";
    public const string PublicKeyFileName = "feed-signing.pub.json";

    public static int Publish(IReadOnlyList<string> args, ILoggerFactory loggers)
    {
        var line = CommandLine.Parse(args, ["source", "packages", "out", "key", "state", "forget-removed-before"], ["unsigned"]);
        var options = new PublishOptions
        {
            Source = line.Required("source"),
            Packages = line.Required("packages"),
            Out = line.Required("out"),
            State = line.Optional("state"),
            Unsigned = line.Has("unsigned"),
            KeyFiles = line.All("key"),
            ForgetRemovedBefore = line.Optional("forget-removed-before") is { } date ? ParseDate(date) : null,
        };

        // Refuse bad folders before asking for a password
        FeedPublisher.ResolvePaths(options);
        if (options.Unsigned && options.KeyFiles.Count > 0)
        {
            throw new UsageException("--unsigned can't be combined with --key.");
        }

        var signers = new List<P256SigningKey>();
        try
        {
            foreach (var keyFile in options.KeyFiles)
            {
                signers.Add(LoadKey(keyFile, loggers.CreateLogger("Publisher")));
            }

            var summary = new FeedPublisher(TimeProvider.System, loggers.CreateLogger<FeedPublisher>()).Publish(options, signers);
            PrintSummary(summary, loggers.CreateLogger("Publisher"));
            return ExitCodes.Success;
        }
        finally
        {
            signers.ForEach(s => s.Dispose());
        }
    }

    public static int Keygen(IReadOnlyList<string> args, ILoggerFactory loggers)
    {
        var line = CommandLine.Parse(args, ["out"], []);
        var log = loggers.CreateLogger("Publisher");
        var directory = Path.GetFullPath(line.Optional("out") ?? ".");
        var keyPath = Path.Combine(directory, PrivateKeyFileName);
        var publicPath = Path.Combine(directory, PublicKeyFileName);
        if (File.Exists(keyPath) || File.Exists(publicPath))
        {
            throw new UsageException($"{keyPath} or {publicPath} already exists; refusing to overwrite a key.");
        }

        var password = Passwords.ForNewKey();
        using var key = P256SigningKey.Generate();
        Directory.CreateDirectory(directory);
        File.WriteAllText(keyPath, key.ExportEncryptedPem(password));
        File.WriteAllText(publicPath, key.PublicKey.ToJson() + Environment.NewLine);

        log.LogInformation($"Private key: {keyPath}");
        log.LogInformation("  Keep it offline and out of git. Back it up; a lost key can't sign updates for installed launchers.");
        log.LogInformation($"Public key:  {publicPath}");
        log.LogInformation("Add this to TrustedPublicKeys in src/OpenShardLauncher.Client/Resources/launcher.json:");
        log.LogInformation(key.PublicKey.ToJson());
        return ExitCodes.Success;
    }

    public static int Verify(IReadOnlyList<string> args, ILoggerFactory loggers)
    {
        var line = CommandLine.Parse(args, ["feed", "pub"], []);
        var log = loggers.CreateLogger("Publisher");
        var feed = line.Required("feed");
        if (!Directory.Exists(feed))
        {
            throw new UsageException($"--feed {feed} does not exist.");
        }

        var keys = line.All("pub").Select(ReadPublicKey).ToList();
        if (keys.Count == 0)
        {
            log.LogWarning("No --pub given: checking an unsigned feed.");
        }

        var result = FeedVerifier.Verify(feed, keys);
        foreach (var problem in result.Problems)
        {
            log.LogError(problem);
        }

        if (!result.IsValid)
        {
            log.LogError($"Feed has {result.Problems.Count} problem(s).");
            return ExitCodes.VerifyFailed;
        }

        log.LogInformation($"Feed is valid: {result.FilesChecked} file(s) and {result.PackagesChecked} package(s) checked.");
        return ExitCodes.Success;
    }

    private static P256SigningKey LoadKey(string keyFile, ILogger log)
    {
        var pem = File.ReadAllText(keyFile);
        if (!P256SigningKey.IsEncryptedPem(pem))
        {
            log.LogWarning($"{keyFile} is not password-protected. Re-create it with keygen.");
            return P256SigningKey.ImportPem(pem, null);
        }

        return P256SigningKey.ImportPem(pem, Passwords.ForExistingKey(keyFile));
    }

    // A launcher.json-format key ({ "alg", "key" }) or bare base64 of a P-256 key, inline or in a file
    private static TrustedKey ReadPublicKey(string value)
    {
        var text = (File.Exists(value) ? File.ReadAllText(value) : value).Trim();
        return text.StartsWith('{') ? TrustedKey.Parse(text) : new TrustedKey(P256SignatureAlgorithm.AlgTag, text);
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : throw new UsageException($"--forget-removed-before: '{value}' is not a date (use e.g. 2026-01-31).");

    private static void PrintSummary(PublishSummary summary, ILogger log)
    {
        foreach (var name in summary.IgnoredPackages)
        {
            log.LogWarning($"Ignored package {name}: not named like launcher-1.2.0.win-x64.zip");
        }

        foreach (var name in summary.SupersededPackages)
        {
            log.LogInformation($"Skipped package {name}: a newer version of it is published");
        }

        log.LogInformation($"Published version {summary.Version} ({(summary.Signed ? "signed" : "UNSIGNED")}): " +
            $"{summary.FileCount} file(s), {summary.HashedCount} hashed this run.");
        log.LogInformation($"Changed: {summary.ChangedFiles.Count}. Removed: {summary.RemovedFiles.Count}. " +
            $"Blobs added: {summary.BlobsAdded}, pruned: {summary.BlobsPruned}. " +
            $"Packages added: {summary.PackagesAdded.Count}, pruned: {summary.PackagesPruned}.");

        if (summary.NewFiles.Count > 0)
        {
            log.LogInformation($"New files ({summary.NewFiles.Count}); check nothing private is among them:");
            foreach (var name in summary.NewFiles)
            {
                log.LogInformation($"  + {name}");
            }
        }

        foreach (var name in summary.RemovedFiles)
        {
            log.LogInformation($"  - {name} (players' copies will be deleted)");
        }

        foreach (var name in summary.PackagesAdded)
        {
            log.LogInformation($"  package {name}");
        }

        log.LogInformation($"To upload: {summary.BytesToUpload:N0} bytes.");
        log.LogInformation("Upload safely: stop the web server while uploading, or upload in three passes: " +
            "(1) blobs/ and packages/ without deleting, (2) packages/manifest.* then files.sig and files.json, " +
            "(3) delete pruned blobs and packages.");
    }
}
