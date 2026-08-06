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
}
