using System.Text;

namespace OpenShardLauncher.Shared.Signing;

public enum SignatureStatus
{
    Missing, // No signature file at all. Acceptable only under AllowUnsignedFeed
    Invalid, // A signature file that is malformed or that no trusted key verifies. Never acceptable
    Valid,   // At least one line verifies with a trusted key of its algorithm
}

// Signatures for files.sig and manifest.sig: one line per signature, "<alg>:<base64>", over the exact bytes of the
// signed file. Several lines let a feed be signed with an old and a new key (or algorithm) at once. Every line must
// be tagged; lines whose algorithm this build doesn't know or the platform doesn't support are skipped, so adding an
// algorithm later needs no format change. Stable once released (docs/feed-format.md).
public static class FeedSigning
{
    // ML-DSA can join this list later (check MLDsa.IsSupported; it's false on macOS in .NET 10)
    public static IReadOnlyList<ISignatureAlgorithm> Algorithms { get; } = [new P256SignatureAlgorithm()];

    // One tagged line per signer, each ending in \n
    public static string CreateSignatureFile(ReadOnlySpan<byte> data, IEnumerable<IFeedSigner> signers)
    {
        var text = new StringBuilder();
        foreach (var signer in signers)
        {
            text.Append(signer.Alg).Append(':').Append(Convert.ToBase64String(signer.Sign(data))).Append('\n');
        }

        return text.ToString();
    }

    // signatureFile is null when the signature file doesn't exist
    public static SignatureStatus Verify(ReadOnlySpan<byte> data, string? signatureFile, IEnumerable<TrustedKey> trustedKeys)
    {
        if (signatureFile is null)
        {
            return SignatureStatus.Missing;
        }

        var lines = new List<(string Alg, string Signature)>();
        foreach (var rawLine in signatureFile.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // An untagged line makes the whole file invalid, so a half-finished setup fails loudly
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || !IsValidTag(line.AsSpan(0, colon)))
            {
                return SignatureStatus.Invalid;
            }

            lines.Add((line[..colon], line[(colon + 1)..]));
        }

        var keys = trustedKeys.ToList();
        foreach (var (alg, signatureText) in lines)
        {
            var algorithm = Algorithms.FirstOrDefault(a => a.Tag == alg && a.IsSupported);
            if (algorithm is null || !TryDecode(signatureText, out var signature))
            {
                continue;
            }

            foreach (var key in keys)
            {
                if (key.Alg == alg && TryDecode(key.Key, out var publicKey) && algorithm.Verify(data, signature, publicKey))
                {
                    return SignatureStatus.Valid;
                }
            }
        }

        return SignatureStatus.Invalid;
    }

    // Lowercase letters and digits, e.g. "p256" or "mldsa65"
    public static bool IsValidTag(ReadOnlySpan<char> tag) =>
        tag.Length is > 0 and <= 16 && !tag.ContainsAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789");

    private static bool TryDecode(string base64, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(base64.Trim());
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
