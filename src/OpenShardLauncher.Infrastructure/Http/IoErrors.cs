namespace OpenShardLauncher.Infrastructure.Http;

// Recognizes the disk errors the launcher reports specially. Windows reports them as HRESULTs; on Unix .NET puts the
// errno in HResult.
internal static class IoErrors
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int Enospc = 28;
    private const int EwouldblockLinux = 11;
    private const int EwouldblockMac = 35;

    // Open in another program (usually the game) with sharing that doesn't allow replacing it.
    public static bool IsLocked(IOException e) =>
        e.HResult is ErrorSharingViolation or ErrorLockViolation
        || (!OperatingSystem.IsWindows() && e.HResult == (OperatingSystem.IsMacOS() ? EwouldblockMac : EwouldblockLinux));

    public static bool IsDiskFull(IOException e) =>
        e.HResult is ErrorHandleDiskFull or ErrorDiskFull || (!OperatingSystem.IsWindows() && e.HResult == Enospc);
}
