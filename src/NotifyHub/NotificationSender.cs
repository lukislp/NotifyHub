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
    /// <paramref name="options"/> is optional: <see cref="SendOptions.Channels"/> restricts
    /// delivery to specific channel types (everything else comes back
    /// <see cref="SendOutcome.Skipped"/>), <see cref="SendOptions.MaxConcurrency"/> caps how many
    /// sends run at once (recommended for very large broadcasts). Omit it entirely for the
    /// default behavior: every channel, fully parallel via
    /// <see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/>.</summary>
    public async Task<IReadOnlyList<ChannelSendResult>> SendAsync(
        NotificationMessage message,
        IEnumerable<Subscription> subscriptions,
        SendOptions? options = null,
        CancellationToken ct = default)
    {
        if (options?.MaxConcurrency is not { } maxConcurrency)
        {
            var tasks = subscriptions.Select(subscription => SendOneAsync(subscription, message, options?.Channels, ct));
            return await Task.WhenAll(tasks);
        }

        if (maxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(options), maxConcurrency, $"{nameof(SendOptions.MaxConcurrency)} must be at least 1.");

        using var throttle = new SemaphoreSlim(maxConcurrency);
        var throttledTasks = subscriptions.Select(async subscription =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await SendOneAsync(subscription, message, options.Channels, ct);
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
    /// <paramref name="options"/> behaves exactly as on <see cref="SendAsync"/>.</summary>
    public async IAsyncEnumerable<ChannelSendResult> SendStreamAsync(
        NotificationMessage message,
        IEnumerable<Subscription> subscriptions,
        SendOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (options?.MaxConcurrency is not { } maxConcurrency)
        {
            var tasks = subscriptions.Select(subscription => SendOneAsync(subscription, message, options?.Channels, ct)).ToList();

#if NET9_0_OR_GREATER
            await foreach (var task in Task.WhenEach(tasks).WithCancellation(ct))
                yield return await task;
#else
            // Task.WhenEach is .NET 9+ - fall back to a WhenAny drain loop on net8.0.
            var pending = new List<Task<ChannelSendResult>>(tasks);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                yield return await completed;
            }
#endif
            yield break;
        }

        if (maxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(options), maxConcurrency, $"{nameof(SendOptions.MaxConcurrency)} must be at least 1.");

        // Bounded producer/consumer: keep at most MaxConcurrency sends in flight and start the
        // next one only as a previous one completes - subscriptions is enumerated lazily, so
        // large broadcasts don't allocate/schedule one task per subscription up front.
        var inFlight = new List<Task<ChannelSendResult>>(maxConcurrency);
        using var enumerator = subscriptions.GetEnumerator();
        var hasMore = true;
        while (true)
        {
            while (hasMore && inFlight.Count < maxConcurrency)
            {
                ct.ThrowIfCancellationRequested();
                if (enumerator.MoveNext())
                    inFlight.Add(SendOneAsync(enumerator.Current, message, options.Channels, ct));
                else
                    hasMore = false;
            }

            if (inFlight.Count == 0)
                yield break;

            var completed = await Task.WhenAny(inFlight);
            inFlight.Remove(completed);
            yield return await completed;
        }
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
