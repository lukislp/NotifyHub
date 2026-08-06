using System.Net;
using System.Security.Cryptography;
using NotifyHub.Channels;
using Xunit;

namespace NotifyHub.Tests;

public class WebPushChannelTests
{
    private static VapidKeyProvider CreateVapidKeyProvider() => new(new InMemoryVapidKeyStore(), "mailto:test@example.com");

    // The channel encrypts the payload via aes128gcm (RFC 8291) BEFORE the request goes out - this
    // requires a real P-256 public key (uncompressed, 65 bytes) and a 16-byte auth secret,
    // otherwise encryption fails locally without the fake HttpMessageHandler ever being called.
    private static Subscription CreateSubscription()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdh.ExportParameters(false);
        var uncompressedPoint = new byte[65];
        uncompressedPoint[0] = 0x04;
        p.Q.X!.CopyTo(uncompressedPoint, 1);
        p.Q.Y!.CopyTo(uncompressedPoint, 33);

        var p256dh = Base64Url(uncompressedPoint);
        var auth = Base64Url(RandomNumberGenerator.GetBytes(16));
        return Subscription.WebPush("https://push.example.com/endpoint/abc", p256dh, auth);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void Enabled_IsAlwaysTrue()
    {
        var channel = new WebPushChannel(CreateVapidKeyProvider());
        Assert.True(channel.Enabled);
        Assert.Equal(NotificationChannel.WebPush, channel.Channel);
    }

    [Fact]
    public async Task SendAsync_Returns_Delivered_OnSuccess()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Created);
        var channel = new WebPushChannel(CreateVapidKeyProvider(), new HttpClient(handler));

        var result = await channel.SendAsync(CreateSubscription(), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Delivered, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Returns_Expired_On410Gone()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Gone);
        var channel = new WebPushChannel(CreateVapidKeyProvider(), new HttpClient(handler));

        var result = await channel.SendAsync(CreateSubscription(), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Expired, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Returns_Failed_OnServerError()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.InternalServerError, "boom");
        var channel = new WebPushChannel(CreateVapidKeyProvider(), new HttpClient(handler));

        var result = await channel.SendAsync(CreateSubscription(), new NotificationMessage { Title = "T", Body = "B" });

        Assert.Equal(SendOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenSubscriptionIncomplete()
    {
        var channel = new WebPushChannel(CreateVapidKeyProvider());
        var incomplete = Subscription.Apns("token"); // wrong channel -> no WebPush fields set

        await Assert.ThrowsAsync<ArgumentException>(() =>
            channel.SendAsync(incomplete, new NotificationMessage { Title = "T", Body = "B" }));
    }

    [Fact]
    public async Task SendAsync_Uses24hTtl_ByDefault_AndOmitsUrgencyAndTopic()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Created);
        var channel = new WebPushChannel(CreateVapidKeyProvider(), new HttpClient(handler));

        await channel.SendAsync(CreateSubscription(), new NotificationMessage { Title = "T", Body = "B" });

        var request = handler.Requests[0];
        Assert.Equal("86400", request.Headers.GetValues("TTL").Single());
        Assert.False(request.Headers.Contains("Urgency"));
        Assert.False(request.Headers.Contains("Topic"));
    }

    [Fact]
    public async Task SendAsync_SetsTtlUrgencyAndTopic_WhenConfigured()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Created);
        var channel = new WebPushChannel(CreateVapidKeyProvider(), new HttpClient(handler));
        var message = new NotificationMessage
        {
            Title = "T",
            Body = "B",
            TimeToLive = TimeSpan.FromMinutes(5),
            Priority = NotificationPriority.High,
            CollapseId = "score-42",
        };

        await channel.SendAsync(CreateSubscription(), message);

        var request = handler.Requests[0];
        Assert.Equal("300", request.Headers.GetValues("TTL").Single());
        Assert.Equal("high", request.Headers.GetValues("Urgency").Single());
        Assert.Equal("score-42", request.Headers.GetValues("Topic").Single());
    }

    [Fact]
    public async Task SendAsync_SetsLowUrgency_ForLowPriority()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Created);
        var channel = new WebPushChannel(CreateVapidKeyProvider(), new HttpClient(handler));

        await channel.SendAsync(CreateSubscription(),
            new NotificationMessage { Title = "T", Body = "B", Priority = NotificationPriority.Low });

        Assert.Equal("low", handler.Requests[0].Headers.GetValues("Urgency").Single());
    }
}
