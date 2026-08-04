namespace NotifyHub;

/// <summary>
/// A delivery target for exactly one channel. The host app manages storage/lifecycle itself
/// (its own DB/file) - this library only receives the list of currently relevant subscriptions
/// per send call. <see cref="Id"/> is an optional back-reference to the host app's own record
/// (e.g. its primary key), meaningless to the library itself, so a <see cref="ChannelSendResult"/>
/// can be reported back unambiguously (e.g. to delete an expired subscription there).
/// </summary>
public sealed record Subscription
{
    public required NotificationChannel Channel { get; init; }
    public string? Id { get; init; }

    /// <summary>Browser push endpoint (WebPush).</summary>
    public string? Endpoint { get; init; }
    /// <summary>P256DH key of the browser subscription (WebPush).</summary>
    public string? P256dh { get; init; }
    /// <summary>Auth secret of the browser subscription (WebPush).</summary>
    public string? Auth { get; init; }

    /// <summary>Device token (APNs or FCM, depending on <see cref="Channel"/>).</summary>
    public string? DeviceToken { get; init; }

    /// <summary>Target URL for generic webhook delivery.</summary>
    public string? Url { get; init; }

    /// <summary>Recipient address for email delivery.</summary>
    public string? EmailAddress { get; init; }

    public static Subscription WebPush(string endpoint, string p256dh, string auth, string? id = null) => new()
    {
        Channel = NotificationChannel.WebPush,
        Endpoint = endpoint,
        P256dh = p256dh,
        Auth = auth,
        Id = id,
    };

    public static Subscription Apns(string deviceToken, string? id = null) => new()
    {
        Channel = NotificationChannel.Apns,
        DeviceToken = deviceToken,
        Id = id,
    };

    public static Subscription Fcm(string deviceToken, string? id = null) => new()
    {
        Channel = NotificationChannel.Fcm,
        DeviceToken = deviceToken,
        Id = id,
    };

    public static Subscription Webhook(string url, string? id = null) => new()
    {
        Channel = NotificationChannel.Webhook,
        Url = url,
        Id = id,
    };

    public static Subscription Email(string emailAddress, string? id = null) => new()
    {
        Channel = NotificationChannel.Email,
        EmailAddress = emailAddress,
        Id = id,
    };
}
