using System.Net;
using NotifyHub.Channels;
using Xunit;

namespace NotifyHub.Tests;

public class WebhookChannelTests
{
    [Fact]
    public void Enabled_IsAlwaysTrue()
    {
        Assert.True(new WebhookChannel().Enabled);
    }

    [Fact]
    public async Task SendAsync_Returns_Delivered_OnSuccess()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.OK);
        var channel = new WebhookChannel(new HttpClient(handler));

        var result = await channel.SendAsync(Subscription.Webhook("https://example.com/hook"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Delivered, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Returns_Expired_On404()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.NotFound);
        var channel = new WebhookChannel(new HttpClient(handler));

        var result = await channel.SendAsync(Subscription.Webhook("https://example.com/hook"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Expired, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Returns_Failed_OnServerError()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.InternalServerError, "boom");
        var channel = new WebhookChannel(new HttpClient(handler));

        var result = await channel.SendAsync(Subscription.Webhook("https://example.com/hook"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenUrlMissing()
    {
        var channel = new WebhookChannel();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            channel.SendAsync(Subscription.Apns("tok"), new NotificationMessage { Title = "T", Body = "B" }));
    }
}
