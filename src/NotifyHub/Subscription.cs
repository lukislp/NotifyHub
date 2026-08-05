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

    /// <summary>Optional shared secret for the webhook channel (Webhook). When set, requests
    /// carry an <c>X-NotifyHub-Signature: sha256=&lt;hex&gt;</c> header - an HMAC-SHA256 over the
    /// raw request body - so the receiver can verify the call really came from NotifyHub and
    /// wasn't tampered with. Omit to send unsigned, as before this was introduced.</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>Optional extra HTTP headers sent with every webhook request (Webhook) - e.g. an
    /// <c>Authorization</c> header required by a custom endpoint.</summary>
    public IReadOnlyDictionary<string, string>? WebhookHeaders { get; init; }

    /// <summary>JSON body shape used when posting to <see cref="Url"/> (Webhook). Default:
    /// <see cref="WebhookPayloadFormat.Generic"/> - set this to <see cref="WebhookPayloadFormat.Slack"/>
    /// or <see cref="WebhookPayloadFormat.Discord"/> when the target is one of those services.</summary>
    public WebhookPayloadFormat WebhookFormat { get; init; } = WebhookPayloadFormat.Generic;

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

    public static Subscription Webhook(
        string url,
        string? id = null,
        string? secret = null,
        IReadOnlyDictionary<string, string>? headers = null,
        WebhookPayloadFormat format = WebhookPayloadFormat.Generic) => new()
    {
        Channel = NotificationChannel.Webhook,
        Url = url,
        Id = id,
        WebhookSecret = secret,
        WebhookHeaders = headers,
        WebhookFormat = format,
    };

    public static Subscription Email(string emailAddress, string? id = null) => new()
    {
        Channel = NotificationChannel.Email,
        EmailAddress = emailAddress,
        Id = id,
    };
}
