using System.Net;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public async Task SendAsync_UsesGenericFormat_ByDefault()
    {
        string? capturedBody = null;
        var handler = new FakeHttpMessageHandler().Enqueue(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new WebhookChannel(new HttpClient(handler));

        await channel.SendAsync(Subscription.Webhook("https://example.com/hook"), new NotificationMessage { Title = "Alert", Body = "Something happened" });

        Assert.Contains("\"title\":\"Alert\"", capturedBody);
        Assert.Contains("\"body\":\"Something happened\"", capturedBody);
    }

    [Fact]
    public async Task SendAsync_UsesSlackFormat_WhenConfigured()
    {
        string? capturedBody = null;
        var handler = new FakeHttpMessageHandler().Enqueue(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new WebhookChannel(new HttpClient(handler));
        var subscription = Subscription.Webhook("https://hooks.slack.com/services/x", format: WebhookPayloadFormat.Slack);

        var result = await channel.SendAsync(subscription, new NotificationMessage { Title = "Alert", Body = "Something happened" });

        Assert.Equal(SendOutcome.Delivered, result.Outcome);
        Assert.Contains("\"text\"", capturedBody);
        Assert.Contains("Alert", capturedBody);
        Assert.Contains("Something happened", capturedBody);
        Assert.DoesNotContain("\"title\"", capturedBody);
    }

    [Fact]
    public async Task SendAsync_UsesDiscordFormat_WhenConfigured()
    {
        string? capturedBody = null;
        var handler = new FakeHttpMessageHandler().Enqueue(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new WebhookChannel(new HttpClient(handler));
        var subscription = Subscription.Webhook("https://discord.com/api/webhooks/x", format: WebhookPayloadFormat.Discord);

        var result = await channel.SendAsync(subscription, new NotificationMessage { Title = "Alert", Body = "Something happened" });

        Assert.Equal(SendOutcome.Delivered, result.Outcome);
        Assert.Contains("\"content\"", capturedBody);
        Assert.DoesNotContain("\"title\"", capturedBody);
    }

    [Fact]
    public async Task SendAsync_AddsSignatureHeader_WhenSecretConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new FakeHttpMessageHandler().Enqueue(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new WebhookChannel(new HttpClient(handler));
        var subscription = Subscription.Webhook("https://example.com/hook", secret: "top-secret");

        await channel.SendAsync(subscription, new NotificationMessage { Title = "T", Body = "B" });

        var signatureHeader = capturedRequest!.Headers.GetValues("X-NotifyHub-Signature").Single();
        var expected = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes("top-secret"), Encoding.UTF8.GetBytes(capturedBody!)));
        Assert.Equal(expected, signatureHeader);
    }

    [Fact]
    public async Task SendAsync_OmitsSignatureHeader_WhenNoSecretConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler().Enqueue(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new WebhookChannel(new HttpClient(handler));

        await channel.SendAsync(Subscription.Webhook("https://example.com/hook"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.False(capturedRequest!.Headers.Contains("X-NotifyHub-Signature"));
    }

    [Fact]
    public async Task SendAsync_IncludesCustomHeaders_WhenConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler().Enqueue(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new WebhookChannel(new HttpClient(handler));
        var subscription = Subscription.Webhook(
            "https://example.com/hook",
            headers: new Dictionary<string, string> { ["Authorization"] = "Bearer token123" });

        await channel.SendAsync(subscription, new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal("Bearer token123", capturedRequest!.Headers.GetValues("Authorization").Single());
    }
}
