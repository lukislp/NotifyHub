namespace NotifyHub;

/// <summary>Delivery channel of a <see cref="Subscription"/>.</summary>
public enum NotificationChannel
{
    WebPush,
    Apns,
    Fcm,
    Webhook,
    Email,
}
