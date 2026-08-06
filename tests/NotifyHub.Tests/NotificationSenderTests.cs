using Xunit;

namespace NotifyHub.Tests;

public class NotificationSenderTests
{
    private static readonly NotificationMessage Message = new() { Title = "T", Body = "B" };

    [Fact]
    public async Task SendAsync_DispatchesEachSubscription_ToItsOwnChannel()
    {
        var webPush = new FakeChannel(NotificationChannel.WebPush);
        var apns = new FakeChannel(NotificationChannel.Apns);
        var sender = new NotificationSender([webPush, apns]);

        var subscriptions = new[]
        {
            Subscription.WebPush("endpoint", "p256dh", "auth"),
            Subscription.Apns("token"),
        };

        var results = await sender.SendAsync(Message, subscriptions);

        Assert.Equal(1, webPush.CallCount);
        Assert.Equal(1, apns.CallCount);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(SendOutcome.Delivered, r.Outcome));
    }

    [Fact]
    public async Task SendAsync_SimultaneouslySendsAcrossMultipleChannels_ForSameLogicalMessage()
    {
        // A user with a browser AND an iPhone: a single SendAsync call,
        // both channels get the message at the same time (Task.WhenAll).
        var webPush = new FakeChannel(NotificationChannel.WebPush);
        var apns = new FakeChannel(NotificationChannel.Apns);
        var sender = new NotificationSender([webPush, apns]);

        var subscriptions = new[]
        {
            Subscription.WebPush("endpoint", "p256dh", "auth", id: "browser-1"),
            Subscription.Apns("token", id: "iphone-1"),
        };

        var results = await sender.SendAsync(Message, subscriptions);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Subscription.Id == "browser-1" && r.Outcome == SendOutcome.Delivered);
        Assert.Contains(results, r => r.Subscription.Id == "iphone-1" && r.Outcome == SendOutcome.Delivered);
    }

    [Fact]
    public async Task SendAsync_ReturnsSkipped_WhenChannelDisabled()
    {
        var apns = new FakeChannel(NotificationChannel.Apns, enabled: false);
        var sender = new NotificationSender([apns]);

        var results = await sender.SendAsync(Message, [Subscription.Apns("token")]);

        Assert.Equal(SendOutcome.Skipped, results[0].Outcome);
        Assert.Equal(0, apns.CallCount); // SendAsync is never called at all when the channel is disabled.
    }

    [Fact]
    public async Task SendAsync_ReturnsSkipped_WhenNoChannelRegisteredForThatType()
    {
        var sender = new NotificationSender([]); // no channels registered

        var results = await sender.SendAsync(Message, [Subscription.Fcm("token")]);

        Assert.Equal(SendOutcome.Skipped, results[0].Outcome);
        Assert.NotNull(results[0].Error);
    }

    [Fact]
    public async Task SendAsync_ReturnsFailed_WhenChannelThrows()
    {
        var throwingChannel = new ThrowingChannel(NotificationChannel.Webhook);
        var sender = new NotificationSender([throwingChannel]);

        var results = await sender.SendAsync(Message, [Subscription.Webhook("https://example.com")]);

        Assert.Equal(SendOutcome.Failed, results[0].Outcome);
    }

    [Fact]
    public async Task SendAsync_SendsToEveryChannel_WhenNoChannelFilterGiven()
    {
        var webPush = new FakeChannel(NotificationChannel.WebPush);
        var apns = new FakeChannel(NotificationChannel.Apns);
        var sender = new NotificationSender([webPush, apns]);

        var subscriptions = new[]
        {
            Subscription.WebPush("endpoint", "p256dh", "auth"),
            Subscription.Apns("token"),
        };

        var results = await sender.SendAsync(Message, subscriptions);

        Assert.Equal(1, webPush.CallCount);
        Assert.Equal(1, apns.CallCount);
        Assert.All(results, r => Assert.Equal(SendOutcome.Delivered, r.Outcome));
    }

    [Fact]
    public async Task SendAsync_RestrictsDelivery_ToChannelAllowList()
    {
        var webPush = new FakeChannel(NotificationChannel.WebPush);
        var apns = new FakeChannel(NotificationChannel.Apns);
        var sender = new NotificationSender([webPush, apns]);

        var subscriptions = new[]
        {
            Subscription.WebPush("endpoint", "p256dh", "auth", id: "browser-1"),
            Subscription.Apns("token", id: "iphone-1"),
        };

        var results = await sender.SendAsync(Message, subscriptions, channels: [NotificationChannel.WebPush]);

        Assert.Equal(1, webPush.CallCount);
        Assert.Equal(0, apns.CallCount); // excluded by the filter, never even called
        Assert.Equal(2, results.Count); // still one result per subscription
        Assert.Equal(SendOutcome.Delivered, results.Single(r => r.Subscription.Id == "browser-1").Outcome);
        Assert.Equal(SendOutcome.Skipped, results.Single(r => r.Subscription.Id == "iphone-1").Outcome);
    }

    [Fact]
    public async Task SendAsync_EmptyChannelFilter_SkipsEverything()
    {
        var webPush = new FakeChannel(NotificationChannel.WebPush);
        var sender = new NotificationSender([webPush]);

        var results = await sender.SendAsync(Message, [Subscription.WebPush("endpoint", "p256dh", "auth")], channels: []);

        Assert.Equal(0, webPush.CallCount);
        Assert.Equal(SendOutcome.Skipped, results[0].Outcome);
    }

    [Fact]
    public async Task SendAsync_LimitsConcurrency_WhenMaxConcurrencySet()
    {
        var tracker = new ConcurrencyTrackingChannel(NotificationChannel.WebPush, TimeSpan.FromMilliseconds(50));
        var sender = new NotificationSender([tracker]);
        var subscriptions = Enumerable.Range(0, 10).Select(i => Subscription.WebPush($"endpoint{i}", "p256dh", "auth")).ToList();

        var results = await sender.SendAsync(Message, subscriptions, maxConcurrency: 2);

        Assert.Equal(10, results.Count);
        Assert.True(tracker.MaxObservedConcurrency <= 2, $"Expected max concurrency <= 2 but was {tracker.MaxObservedConcurrency}.");
    }

    [Fact]
    public async Task SendAsync_AllowsFullConcurrency_WhenMaxConcurrencyNotSet()
    {
        var tracker = new ConcurrencyTrackingChannel(NotificationChannel.WebPush, TimeSpan.FromMilliseconds(50));
        var sender = new NotificationSender([tracker]);
        var subscriptions = Enumerable.Range(0, 10).Select(i => Subscription.WebPush($"endpoint{i}", "p256dh", "auth")).ToList();

        await sender.SendAsync(Message, subscriptions);

        Assert.Equal(10, tracker.MaxObservedConcurrency);
    }

    [Fact]
    public async Task SendStreamAsync_YieldsOneResultPerSubscription()
    {
        var webPush = new FakeChannel(NotificationChannel.WebPush);
        var sender = new NotificationSender([webPush]);
        var subscriptions = Enumerable.Range(0, 5).Select(i => Subscription.WebPush($"endpoint{i}", "p256dh", "auth", id: $"sub-{i}")).ToList();

        var results = new List<ChannelSendResult>();
        await foreach (var result in sender.SendStreamAsync(Message, subscriptions))
            results.Add(result);

        Assert.Equal(5, results.Count);
        Assert.Equal(5, webPush.CallCount);
        // Completion order is not guaranteed - correlate via subscription IDs instead.
        Assert.Equal(
            subscriptions.Select(s => s.Id).Order(),
            results.Select(r => r.Subscription.Id).Order());
        Assert.All(results, r => Assert.Equal(SendOutcome.Delivered, r.Outcome));
    }

    [Fact]
    public async Task SendStreamAsync_RespectsChannelFilterAndConcurrency()
    {
        var tracker = new ConcurrencyTrackingChannel(NotificationChannel.WebPush, TimeSpan.FromMilliseconds(50));
        var apns = new FakeChannel(NotificationChannel.Apns);
        var sender = new NotificationSender([tracker, apns]);
        var subscriptions = Enumerable.Range(0, 6)
            .Select(i => Subscription.WebPush($"endpoint{i}", "p256dh", "auth"))
            .Append(Subscription.Apns("token", id: "iphone-1"))
            .ToList();

        var results = new List<ChannelSendResult>();
        await foreach (var result in sender.SendStreamAsync(Message, subscriptions, channels: [NotificationChannel.WebPush], maxConcurrency: 2))
            results.Add(result);

        Assert.Equal(7, results.Count);
        Assert.Equal(0, apns.CallCount);
        Assert.Equal(SendOutcome.Skipped, results.Single(r => r.Subscription.Id == "iphone-1").Outcome);
        Assert.True(tracker.MaxObservedConcurrency <= 2, $"Expected max concurrency <= 2 but was {tracker.MaxObservedConcurrency}.");
    }

    private sealed class ConcurrencyTrackingChannel(NotificationChannel channel, TimeSpan delay) : Abstractions.INotificationChannel
    {
        private int _current;
        private readonly Lock _lock = new();

        public int MaxObservedConcurrency { get; private set; }
        public NotificationChannel Channel { get; } = channel;
        public bool Enabled => true;

        public async Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default)
        {
            var current = Interlocked.Increment(ref _current);
            lock (_lock)
            {
                if (current > MaxObservedConcurrency)
                    MaxObservedConcurrency = current;
            }
            await Task.Delay(delay, ct);
            Interlocked.Decrement(ref _current);
            return new ChannelSendResult(subscription, SendOutcome.Delivered);
        }
    }

    private sealed class ThrowingChannel(NotificationChannel channel) : Abstractions.INotificationChannel
    {
        public NotificationChannel Channel { get; } = channel;
        public bool Enabled => true;
        public Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("Deliberate test failure.");
    }
}
