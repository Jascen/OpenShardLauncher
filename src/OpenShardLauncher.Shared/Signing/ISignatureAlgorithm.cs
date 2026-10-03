namespace OpenShardLauncher.Shared.Signing;

// The verifying half of one signature algorithm, selected by the tag at the start of a signature line
public interface ISignatureAlgorithm
{
    string Tag { get; }

    // False where the platform can't verify it (e.g. ML-DSA on macOS); its lines are then skipped
    bool IsSupported { get; }

    // False for a malformed key or signature as well as a wrong one
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey);
}

// The signing half, used only by the Publisher on the machine that holds the private key
public interface IFeedSigner
{
    string Alg { get; }

    TrustedKey PublicKey { get; }

    byte[] Sign(ReadOnlySpan<byte> data);
}
