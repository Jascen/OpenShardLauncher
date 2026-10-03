namespace OpenShardLauncher.Publisher.Cli;

// Documented in the Publisher README; scripts depend on them
public static class ExitCodes
{
    public const int Success = 0;
    public const int Failed = 1; // Refused or failed (the message says why); a refused publish leaves the feed unchanged
    public const int Usage = 2; // Unknown command, option or missing value
    public const int VerifyFailed = 3; // verify found problems in the feed
}
