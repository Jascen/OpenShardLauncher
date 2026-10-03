namespace OpenShardLauncher.Publisher.Publishing;

public sealed record PublishOptions
{
    // Game files, laid out as they should appear in the install folder
    public required string Source { get; init; }

    // Package zips named {role}-{version}.{rid}.zip; the newest per role and platform is published
    public required string Packages { get; init; }

    // The feed folder to create or update
    public required string Out { get; init; }

    // The hash cache folder. Default: <Out>/../.publisher-state. Never inside the feed or the source.
    public string? State { get; init; }

    // Publish without files.sig and manifest.sig (for launchers built with AllowUnsignedFeed)
    public bool Unsigned { get; init; }

    // The private key files behind the signers, so a key inside --source can be refused
    public IReadOnlyList<string> KeyFiles { get; init; } = [];

    // Drop removed-list entries removed before this time
    public DateTimeOffset? ForgetRemovedBefore { get; init; }
}

// Raised when a publish is refused or can't be completed. The feed is unchanged unless the message says otherwise.
public sealed class PublishException(string message) : Exception(message);
