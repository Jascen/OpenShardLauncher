using System.Security.Cryptography;

namespace OpenShardLauncher.Shared.Files;

// SHA-256 as 64 lowercase hex characters, the form used in files.json, the package manifest and blob names
public static class Sha256Hex
{
    public const int Length = 64;

    public static string Of(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    public static string Of(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string OfFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return Of(stream);
    }

    // Lowercase only, so a hash has exactly one spelling and can be used as a file name on any file system
    public static bool IsValid(string? value) =>
        value is { Length: Length } && value.All(char.IsAsciiHexDigitLower);
}
