namespace NotifyHub.Abstractions;

/// <summary>
/// A delivery channel (WebPush/APNs/FCM/webhook/email). Implementations are stateless with
/// respect to individual subscriptions - configuration (credentials) comes exclusively from the
/// host app via the constructor.
/// </summary>
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    /// <summary>False when the configuration required for this channel is missing -
    /// <see cref="SendAsync"/> is then never called by <see cref="NotificationSender"/>, the
    /// result is directly <see cref="SendOutcome.Skipped"/>.</summary>
    bool Enabled { get; }

    Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default);
}
