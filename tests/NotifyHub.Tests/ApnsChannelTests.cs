using System.Net;
using System.Security.Cryptography;
using NotifyHub.Channels;
using NotifyHub.Options;
using Xunit;

namespace NotifyHub.Tests;

public class ApnsChannelTests
{
    private static string CreateTempP8Key()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportPkcs8PrivateKeyPem();
        var path = Path.GetTempFileName();
        File.WriteAllText(path, pem);
        return path;
    }

    private static ApnsOptions CreateOptions(string keyPath) => new()
    {
        KeyPath = keyPath,
        KeyId = "KEYID123",
        TeamId = "TEAMID123",
        BundleId = "com.example.app",
        UseSandbox = true,
    };

    [Fact]
    public void Enabled_IsFalse_WithoutOptions()
    {
        var channel = new ApnsChannel(null);
        Assert.False(channel.Enabled);
    }

    [Fact]
    public async Task SendAsync_ReturnsSkipped_WhenDisabled()
    {
        var channel = new ApnsChannel(null);
        var result = await channel.SendAsync(Subscription.Apns("tok"), new NotificationMessage { Title = "T", Body = "B" });
        Assert.Equal(SendOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Returns_Delivered_OnSuccess()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.OK);
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));

            var result = await channel.SendAsync(Subscription.Apns("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

            Assert.Equal(SendOutcome.Delivered, result.Outcome);
            Assert.Contains("bearer ", handler.Requests[0].Headers.GetValues("authorization").First());
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public async Task SendAsync_Returns_Expired_OnGone()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Gone, "{\"reason\":\"Unregistered\"}");
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));

            var result = await channel.SendAsync(Subscription.Apns("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

            Assert.Equal(SendOutcome.Expired, result.Outcome);
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public async Task SendAsync_Returns_Expired_OnBadDeviceToken()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest, "{\"reason\":\"BadDeviceToken\"}");
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));

            var result = await channel.SendAsync(Subscription.Apns("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

            Assert.Equal(SendOutcome.Expired, result.Outcome);
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public async Task SendAsync_Returns_Failed_OnOtherError()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.InternalServerError, "{\"reason\":\"InternalServerError\"}");
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));

            var result = await channel.SendAsync(Subscription.Apns("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

            Assert.Equal(SendOutcome.Failed, result.Outcome);
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public async Task GetJwt_IsCached_AcrossCalls()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.OK).Enqueue(HttpStatusCode.OK);
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));

            await channel.SendAsync(Subscription.Apns("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });
            await channel.SendAsync(Subscription.Apns("devicetoken"), new NotificationMessage { Title = "T", Body = "B" });

            var jwt1 = handler.Requests[0].Headers.GetValues("authorization").First();
            var jwt2 = handler.Requests[1].Headers.GetValues("authorization").First();
            Assert.Equal(jwt1, jwt2);
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public async Task SendAsync_IncludesDataAndUrl_AsTopLevelKeys()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            string? capturedBody = null;
            var handler = new FakeHttpMessageHandler().Enqueue(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));
            var message = new NotificationMessage
            {
                Title = "T",
                Body = "B",
                Url = "https://example.com/deep-link",
                Data = new Dictionary<string, string> { ["entityId"] = "42" },
            };

            await channel.SendAsync(Subscription.Apns("devicetoken"), message);

            // Regression test: Data/Url used to be silently dropped for APNs - Apple's convention
            // is custom keys as top-level siblings of "aps", not nested inside it.
            Assert.Contains("\"entityId\":\"42\"", capturedBody);
            Assert.Contains("\"url\":\"https://example.com/deep-link\"", capturedBody);
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public async Task SendAsync_IncludesBadgeAndCustomSound_WhenSet()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            string? capturedBody = null;
            var handler = new FakeHttpMessageHandler().Enqueue(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));
            var message = new NotificationMessage { Title = "T", Body = "B", Badge = 7, Sound = "chime.caf" };

            await channel.SendAsync(Subscription.Apns("devicetoken"), message);

            Assert.Contains("\"badge\":7", capturedBody);
            Assert.Contains("\"sound\":\"chime.caf\"", capturedBody);
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public async Task SendAsync_SendsBackgroundPush_WhenSilent()
    {
        var keyPath = CreateTempP8Key();
        try
        {
            string? capturedBody = null;
            HttpRequestMessage? capturedRequest = null;
            var handler = new FakeHttpMessageHandler().Enqueue(req =>
            {
                capturedRequest = req;
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var channel = new ApnsChannel(CreateOptions(keyPath), new HttpClient(handler));

            await channel.SendAsync(Subscription.Apns("devicetoken"), new NotificationMessage { Title = "T", Body = "B", Silent = true });

            Assert.Contains("\"content-available\":1", capturedBody);
            Assert.DoesNotContain("\"alert\"", capturedBody);
            Assert.DoesNotContain("\"sound\"", capturedBody);
            Assert.Equal("background", capturedRequest!.Headers.GetValues("apns-push-type").Single());
            Assert.Equal("5", capturedRequest.Headers.GetValues("apns-priority").Single());
        }
        finally { File.Delete(keyPath); }
    }
}
