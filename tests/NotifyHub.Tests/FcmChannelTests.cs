using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NotifyHub.Channels;
using NotifyHub.Options;
using Xunit;

namespace NotifyHub.Tests;

public class FcmChannelTests
{
    private static FcmOptions CreateOptions(string tokenUri = "https://fake-oauth.example.com/token")
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        var serviceAccountJson = JsonSerializer.Serialize(new
        {
            client_email = "test@fake-project.iam.gserviceaccount.com",
            private_key = privateKeyPem,
            token_uri = tokenUri,
        });
        return new FcmOptions { ServiceAccountJson = serviceAccountJson, ProjectId = "fake-project" };
    }

    private static FakeHttpMessageHandler EnqueueTokenResponse(FakeHttpMessageHandler handler)
    {
        handler.Enqueue(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { access_token = "fake-access-token", expires_in = 3600 })),
        });
        return handler;
    }

    [Fact]
    public void Enabled_IsFalse_WithoutOptions()
    {
        var channel = new FcmChannel(null);
        Assert.False(channel.Enabled);
    }

    [Fact]
    public async Task SendAsync_ReturnsSkipped_WhenDisabled()
    {
        var channel = new FcmChannel(null);
        var result = await channel.SendAsync(Subscription.Fcm("tok"), new NotificationMessage { Title = "T", Body = "B" });
        Assert.Equal(SendOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Returns_Delivered_OnSuccess()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        handler.Enqueue(HttpStatusCode.OK, "{\"name\":\"projects/fake-project/messages/1\"}");
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));

        var result = await channel.SendAsync(Subscription.Fcm("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Delivered, result.Outcome);
        // The first request went to the token endpoint, the second to FCM itself.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("messages:send", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task SendAsync_Returns_Expired_OnUnregistered()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        handler.Enqueue(HttpStatusCode.NotFound, "{\"error\":{\"status\":\"UNREGISTERED\"}}");
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));

        var result = await channel.SendAsync(Subscription.Fcm("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Expired, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Returns_Failed_OnOtherError()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        handler.Enqueue(HttpStatusCode.InternalServerError, "{\"error\":{\"status\":\"INTERNAL\"}}");
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));

        var result = await channel.SendAsync(Subscription.Fcm("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task AccessToken_IsCached_AcrossCalls()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        handler.Enqueue(HttpStatusCode.OK).Enqueue(HttpStatusCode.OK);
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));

        await channel.SendAsync(Subscription.Fcm("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });
        await channel.SendAsync(Subscription.Fcm("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

        // Only ONE token request overall (1x token + 2x send = 3 requests), since the token is cached.
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_IncludesImage_WhenSet()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        string? capturedBody = null;
        handler.Enqueue(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));
        var message = new NotificationMessage { Title = "T", Body = "B", ImageUrl = "https://example.com/pic.png" };

        await channel.SendAsync(Subscription.Fcm("devicetoken"), message);

        Assert.Contains("\"image\":\"https://example.com/pic.png\"", capturedBody);
    }

    [Fact]
    public async Task SendAsync_OmitsNotification_WhenSilent()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        string? capturedBody = null;
        handler.Enqueue(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));
        var message = new NotificationMessage
        {
            Title = "T",
            Body = "B",
            Silent = true,
            Data = new Dictionary<string, string> { ["syncToken"] = "abc" },
        };

        await channel.SendAsync(Subscription.Fcm("devicetoken"), message);

        Assert.DoesNotContain("\"notification\"", capturedBody);
        Assert.Contains("\"syncToken\":\"abc\"", capturedBody);
    }

    [Fact]
    public async Task SendAsync_OmitsAndroidConfig_ByDefault()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        string? capturedBody = null;
        handler.Enqueue(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));

        await channel.SendAsync(Subscription.Fcm("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

        Assert.DoesNotContain("\"android\"", capturedBody);
    }

    [Fact]
    public async Task SendAsync_IncludesAndroidTtlPriorityAndCollapse_WhenConfigured()
    {
        var handler = EnqueueTokenResponse(new FakeHttpMessageHandler());
        string? capturedBody = null;
        handler.Enqueue(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var channel = new FcmChannel(CreateOptions(), new HttpClient(handler));
        var message = new NotificationMessage
        {
            Title = "T",
            Body = "B",
            TimeToLive = TimeSpan.FromHours(1),
            Priority = NotificationPriority.High,
            CollapseId = "score-42",
        };

        await channel.SendAsync(Subscription.Fcm("devicetoken"), message);

        Assert.Contains("\"ttl\":\"3600s\"", capturedBody);
        Assert.Contains("\"priority\":\"HIGH\"", capturedBody);
        Assert.Contains("\"collapse_key\":\"score-42\"", capturedBody);
        Assert.Contains("\"tag\":\"score-42\"", capturedBody);
    }
}
