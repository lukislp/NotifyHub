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

    public async Task<IReadOnlyList<ChannelSendResult>> SendAsync(
        NotificationMessage message, IEnumerable<Subscription> subscriptions, CancellationToken ct = default)
    {
        var tasks = subscriptions.Select(subscription => SendOneAsync(subscription, message, ct));
        return await Task.WhenAll(tasks);
    }

    private async Task<ChannelSendResult> SendOneAsync(Subscription subscription, NotificationMessage message, CancellationToken ct)
    {
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
