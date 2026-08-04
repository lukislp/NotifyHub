using NotifyHub.Abstractions;

namespace NotifyHub.Tests;

public sealed class FakeChannel(NotificationChannel channel, bool enabled = true, SendOutcome outcome = SendOutcome.Delivered) : INotificationChannel
{
    public int CallCount { get; private set; }
    public NotificationChannel Channel { get; } = channel;
    public bool Enabled { get; } = enabled;

    public Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(new ChannelSendResult(subscription, outcome));
    }
}
