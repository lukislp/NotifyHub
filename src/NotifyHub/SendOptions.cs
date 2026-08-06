namespace NotifyHub;

/// <summary>
/// Optional per-call options for <see cref="NotificationSender.SendAsync"/> and
/// <see cref="NotificationSender.SendStreamAsync"/>. Omit entirely (the default) for the
/// zero-config behavior: send to every subscription across every channel, fully parallel.
/// </summary>
public sealed record SendOptions
{
    /// <summary>Optional channel allow-list: when set, subscriptions whose
    /// <see cref="NotificationChannel"/> isn't in the list are reported as
    /// <see cref="SendOutcome.Skipped"/> without being sent - a convenience for "send to these
    /// subscriptions, but only via WebPush" without having to filter the subscription list
    /// yourself. Leave unset to send across every channel.</summary>
    public IReadOnlyCollection<NotificationChannel>? Channels { get; init; }

    /// <summary>Optional cap on how many sends run at once. Left unset, every subscription is
    /// sent in full parallel - fine for small/medium subscriber counts. For a large broadcast
    /// (e.g. tens of thousands of subscriptions), firing every send at once can exhaust the
    /// local connection pool and trip provider-side rate limits (APNs/FCM throttle
    /// aggressively) - set a cap to bound how many HTTP calls are in flight simultaneously.</summary>
    public int? MaxConcurrency { get; init; }
}
