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
}
