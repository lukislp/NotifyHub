namespace NotifyHub.Options;

/// <summary>
/// Credentials for Apple Push Notifications (token-based authentication with a p8 key -
/// recommended by Apple, one key is valid for all of the team's apps). Without fully populated
/// values, <see cref="Channels.ApnsChannel"/> stays disabled (a silent no-op).
/// </summary>
public sealed record ApnsOptions
{
    /// <summary>Path to the .p8 key file from the Apple Developer portal.</summary>
    public required string KeyPath { get; init; }
    /// <summary>Key ID of the p8 key.</summary>
    public required string KeyId { get; init; }
    /// <summary>Apple Team ID.</summary>
    public required string TeamId { get; init; }
    /// <summary>Bundle ID of the target app (apns-topic).</summary>
    public required string BundleId { get; init; }
    /// <summary>true for builds signed with a development profile (their tokens belong to the sandbox environment).</summary>
    public bool UseSandbox { get; init; }
    /// <summary>Overrides the target URL (only relevant for tests).</summary>
    public string? Endpoint { get; init; }
}
