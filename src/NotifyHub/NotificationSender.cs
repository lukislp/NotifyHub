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
    /// existed.
    ///
    /// <paramref name="maxConcurrency"/> is an optional cap on how many sends run at once. Left
    /// unset (the default), every subscription is sent in full parallel via
    /// <see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/>, unchanged from before this
    /// parameter existed - fine for small/medium subscriber counts. For a large broadcast (e.g.
    /// tens of thousands of subscriptions), firing every send at once can exhaust the local
    /// connection pool and trip provider-side rate limits (APNs/FCM throttle aggressively) -
    /// set a cap to bound how many HTTP calls are in flight simultaneously.</summary>
    public async Task<IReadOnlyList<ChannelSendResult>> SendAsync(
        NotificationMessage message,
        IEnumerable<Subscription> subscriptions,
        IReadOnlyCollection<NotificationChannel>? channels = null,
        int? maxConcurrency = null,
        CancellationToken ct = default)
    {
        if (maxConcurrency is null)
        {
            var tasks = subscriptions.Select(subscription => SendOneAsync(subscription, message, channels, ct));
            return await Task.WhenAll(tasks);
        }

        using var throttle = new SemaphoreSlim(maxConcurrency.Value);
        var throttledTasks = subscriptions.Select(async subscription =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await SendOneAsync(subscription, message, channels, ct);
            }
            finally
            {
                throttle.Release();
            }
        });
        return await Task.WhenAll(throttledTasks);
    }

    /// <summary>Streaming variant of <see cref="SendAsync"/>: yields each
    /// <see cref="ChannelSendResult"/> as soon as that individual send completes, instead of
    /// waiting for the whole batch. Useful for large broadcasts - progress can be reported and
    /// expired subscriptions cleaned up while the remaining sends are still running. Results
    /// arrive in <b>completion order</b>, not input order; use
    /// <see cref="ChannelSendResult.Subscription"/> (e.g. its <c>Id</c>) to correlate.
    /// <paramref name="channels"/> and <paramref name="maxConcurrency"/> behave exactly as on
    /// <see cref="SendAsync"/>.</summary>
    public async IAsyncEnumerable<ChannelSendResult> SendStreamAsync(
        NotificationMessage message,
        IEnumerable<Subscription> subscriptions,
        IReadOnlyCollection<NotificationChannel>? channels = null,
        int? maxConcurrency = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var throttle = maxConcurrency is { } cap ? new SemaphoreSlim(cap) : null;

        var tasks = subscriptions.Select(async subscription =>
        {
            if (throttle is not null)
                await throttle.WaitAsync(ct);
            try
            {
                return await SendOneAsync(subscription, message, channels, ct);
            }
            finally
            {
                throttle?.Release();
            }
        }).ToList();

        await foreach (var task in Task.WhenEach(tasks).WithCancellation(ct))
            yield return await task;
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
