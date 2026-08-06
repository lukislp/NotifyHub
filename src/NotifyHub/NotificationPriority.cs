namespace NotifyHub;

/// <summary>
/// Delivery urgency of a notification, mapped to each push provider's own priority concept.
/// <see cref="Normal"/> (the default) keeps every provider's standard behavior, exactly as
/// before this type existed - set <see cref="High"/> for time-critical messages (calls, 2FA
/// codes) or <see cref="Low"/> for messages that may wait for a battery-friendly moment
/// (marketing, digests). Not applicable to Email; passed through in the Webhook generic payload.
/// </summary>
public enum NotificationPriority
{
    /// <summary>Provider default: WebPush omits the <c>Urgency</c> header ("normal"), FCM uses
    /// its own default (HIGH for notification messages, NORMAL for data-only), APNs sends
    /// alerts at priority 10 / background pushes at 5.</summary>
    Normal,

    /// <summary>Battery-friendly delivery: WebPush <c>Urgency: low</c>, FCM <c>NORMAL</c>
    /// (Android may delay until the device wakes), APNs priority 5 (delivery at a
    /// power-conserving moment).</summary>
    Low,

    /// <summary>Immediate delivery: WebPush <c>Urgency: high</c>, FCM <c>HIGH</c> (wakes the
    /// device from Doze), APNs priority 10.</summary>
    High,
}
