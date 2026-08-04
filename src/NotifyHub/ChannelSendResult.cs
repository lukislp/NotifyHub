namespace NotifyHub;

/// <summary>Result of a single delivery attempt.</summary>
public enum SendOutcome
{
    Delivered,

    /// <summary>The subscription is no longer valid at the provider (e.g. HTTP 410 Gone for
    /// WebPush, BadDeviceToken for APNs, UNREGISTERED for FCM) - the host app should remove it
    /// from its own storage.</summary>
    Expired,

    /// <summary>Delivery failed (network/provider error) - the subscription remains valid,
    /// a retry may make sense.</summary>
    Failed,

    /// <summary>The channel is not configured (e.g. no APNs key provided) - a deliberate
    /// no-op, not an error.</summary>
    Skipped,
}

/// <summary>Feedback for exactly one <see cref="Subscription"/> after a send operation.</summary>
public sealed record ChannelSendResult(Subscription Subscription, SendOutcome Outcome, string? Error = null);
