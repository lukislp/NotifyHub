using NotifyHub.Abstractions;

namespace NotifyHub;

/// <summary>
/// Central send point: takes a <see cref="NotificationMessage"/> and an arbitrarily mixed list
/// of <see cref="Subscription"/>s and sends across all involved channels at once (browser
/// WebPush, iOS APNs, Android FCM, webhook, email - in parallel via
/// <see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/>) - a user with e.g. a
/// registered browser AND an iPhone gets the message on both at the same time.
/// </summary>
public sealed class NotificationSender
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationChannel> _channels;

    public NotificationSender(IEnumerable<INotificationChannel> channels)
    {
        _channels = channels.ToDictionary(c => c.Channel);
    }

    /// <summary>Sends <paramref name="message"/> to every one of <paramref name="subscriptions"/>,
    /// in parallel, and returns exactly one <see cref="ChannelSendResult"/> per subscription (same
    /// order as the input). Which users/subscriptions are included is entirely up to the caller -
    /// pass only the subscriptions you want targeted (e.g. a single user's, or a hand-picked
    /// subset for a custom targeting rule).
    ///
    /// <paramref name="channels"/> is an optional allow-list: when set, subscriptions whose
    /// <see cref="NotificationChannel"/> isn't in the list are reported as
    /// <see cref="SendOutcome.Skipped"/> without being sent - a convenience for "send to these
    /// subscriptions, but only via WebPush" without having to filter the list yourself. Omit it
    /// (the default) to send across every channel, unchanged from before this parameter
    /// existed.</summary>
    public async Task<IReadOnlyList<ChannelSendResult>> SendAsync(
        NotificationMessage message,
        IEnumerable<Subscription> subscriptions,
        IReadOnlyCollection<NotificationChannel>? channels = null,
        CancellationToken ct = default)
    {
        var tasks = subscriptions.Select(subscription => SendOneAsync(subscription, message, channels, ct));
        return await Task.WhenAll(tasks);
    }

    private async Task<ChannelSendResult> SendOneAsync(
        Subscription subscription, NotificationMessage message, IReadOnlyCollection<NotificationChannel>? channels, CancellationToken ct)
    {
        if (channels is not null && !channels.Contains(subscription.Channel))
            return new ChannelSendResult(subscription, SendOutcome.Skipped, "Channel excluded by caller-specified channel filter.");

        if (!_channels.TryGetValue(subscription.Channel, out var channel))
            return new ChannelSendResult(subscription, SendOutcome.Skipped, $"No channel registered for {subscription.Channel}.");

        if (!channel.Enabled)
            return new ChannelSendResult(subscription, SendOutcome.Skipped);

        try
        {
            return await channel.SendAsync(subscription, message, ct);
        }
        catch (Exception ex)
        {
            return new ChannelSendResult(subscription, SendOutcome.Failed, ex.Message);
        }
    }
}
