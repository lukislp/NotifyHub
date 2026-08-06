namespace NotifyHub;

/// <summary>Channel-independent content of a notification. Every field beyond
/// <see cref="Title"/>/<see cref="Body"/> is optional - each channel uses only the fields it
/// understands and ignores the rest, so the same message can be sent across every channel type
/// without channel-specific branching in the caller.</summary>
public sealed record NotificationMessage
{
    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>Optional target URL to open when the notification is tapped.</summary>
    public string? Url { get; init; }

    /// <summary>Additional, channel-specific payload data (e.g. for deep links). Delivered as
    /// top-level custom keys alongside <c>"aps"</c> for APNs, as the <c>"data"</c> field for FCM,
    /// and as <c>"data"</c> in the WebPush/Webhook (generic format) JSON payload.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>App icon badge count. Maps to APNs <c>aps.badge</c>. Not applicable to
    /// WebPush/FCM/Webhook/Email - ignored there. Leave unset to not touch the app's existing
    /// badge count (Apple's default behavior when this field is omitted).</summary>
    public int? Badge { get; init; }

    /// <summary>Custom notification sound (APNs <c>aps.sound</c>). Defaults to the platform's
    /// standard sound when left unset. Not applicable to WebPush/FCM/Webhook/Email.</summary>
    public string? Sound { get; init; }

    /// <summary>When true, sends a silent/background notification instead of a visible one:
    /// APNs <c>content-available: 1</c> (no <c>alert</c>/<c>sound</c>), FCM a data-only message
    /// (no <c>notification</c> key - <see cref="Data"/> only), WebPush a
    /// <c>Notification(..., { silent: true })</c> hint for the host's own service worker.
    /// Useful for background sync. Default false (a normal, visible notification). Not
    /// applicable to Webhook/Email.</summary>
    public bool Silent { get; init; }

    /// <summary>Optional image/icon URL. Passed through as FCM's <c>notification.image</c> and
    /// included in the WebPush/Webhook (generic format) JSON payload for the host's own service
    /// worker/receiver to use. Not applicable to APNs (rich image attachments there require a
    /// Notification Service Extension on the app side - out of scope for a server-side push) or
    /// Email.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>How long the push provider should keep trying to deliver this message when the
    /// device is offline. Maps to the WebPush <c>TTL</c> header (default when unset: 24h, as
    /// before), APNs <c>apns-expiration</c> (default when unset: Apple's own store-and-forward
    /// policy), and FCM <c>android.ttl</c> (default when unset: FCM's 4-week maximum). Use a
    /// short value for messages that become pointless quickly ("your driver has arrived").
    /// Not applicable to Webhook/Email (delivered immediately or not at all).</summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>Delivery urgency. Default <see cref="NotificationPriority.Normal"/> = every
    /// provider's standard behavior, unchanged from before this field existed. See
    /// <see cref="NotificationPriority"/> for the per-provider mapping.</summary>
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;

    /// <summary>Collapse/replacement key: a new notification with the same ID replaces the
    /// previous one instead of stacking (e.g. a live score - five updates show as one
    /// notification, not five). Maps to the WebPush <c>Topic</c> header (also passed as
    /// <c>tag</c> in the payload for the service worker), APNs <c>apns-collapse-id</c>, and FCM
    /// <c>android.collapse_key</c> + <c>android.notification.tag</c>. WebPush/APNs limit this
    /// to 32/64 bytes respectively - keep it short.</summary>
    public string? CollapseId { get; init; }

    /// <summary>Optional HTML version of <see cref="Body"/> for the email channel. When set,
    /// the email is sent as <c>multipart/alternative</c> with this HTML part plus the plain-text
    /// <see cref="Body"/> as fallback - when unset, a plain-text-only email is sent, as before.
    /// Ignored by every other channel.</summary>
    public string? HtmlBody { get; init; }
}
