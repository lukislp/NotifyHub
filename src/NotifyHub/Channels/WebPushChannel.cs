using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyHub.Abstractions;

namespace NotifyHub.Channels;

/// <summary>
/// Browser push via VAPID (Web Push Protocol, RFC 8291/8292). VAPID keys come from
/// <see cref="VapidKeyProvider"/> (generated automatically, never configured manually) - this
/// channel is therefore always active, with no external account whatsoever.
///
/// Encryption and JWT signing run through <see cref="WebPushCrypto"/> (plain
/// System.Security.Cryptography, no external web push library) - the previously used "WebPush"
/// NuGet package still produces the old draft format (Authorization: "WebPush ...", a separate
/// Crypto-Key header, Content-Encoding "aesgcm"), which Apple's web push implementation rejects
/// with "BadJwtToken" - Chrome/Firefox only accept it for backward-compatibility reasons.
/// </summary>
public sealed class WebPushChannel(VapidKeyProvider vapidKeyProvider, HttpClient? httpClient = null, ILogger<WebPushChannel>? logger = null) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.WebPush;
    public bool Enabled => true;

    public async Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default)
    {
        if (subscription.Endpoint is null || subscription.P256dh is null || subscription.Auth is null)
            throw new ArgumentException("WebPush subscription requires Endpoint, P256dh, and Auth.", nameof(subscription));

        var keys = await vapidKeyProvider.EnsureKeysAsync(ct);
        var client = httpClient;
        var ownsClient = false;
        if (client is null)
        {
            client = new HttpClient();
            ownsClient = true;
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                title = message.Title,
                body = message.Body,
                url = message.Url,
                data = message.Data,
                image = message.ImageUrl,
                silent = message.Silent,
                tag = message.CollapseId,
            });
            var body = WebPushCrypto.EncryptPayload(payload, subscription.P256dh, subscription.Auth);

            var endpoint = new Uri(subscription.Endpoint);
            var audience = $"{endpoint.Scheme}://{endpoint.Host}";
            var jwt = WebPushCrypto.CreateVapidJwt(audience, keys.Subject, keys.PublicKey, keys.PrivateKey);

            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint);
            request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={jwt}, k={keys.PublicKey}");
            // Default TTL of 24h (unchanged) unless the caller sets an explicit TimeToLive.
            var ttlSeconds = (long)(message.TimeToLive?.TotalSeconds ?? 86400);
            request.Headers.Add("TTL", ttlSeconds.ToString());
            if (message.Priority != NotificationPriority.Normal)
                request.Headers.Add("Urgency", message.Priority == NotificationPriority.High ? "high" : "low");
            // RFC 8030 "Topic": a later message with the same topic replaces a still-queued
            // earlier one at the push service (max 32 base64url characters).
            if (message.CollapseId is not null)
                request.Headers.Add("Topic", message.CollapseId);
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return new ChannelSendResult(subscription, SendOutcome.Delivered);

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var error = $"HTTP {(int)response.StatusCode}: {responseBody}";
            if (response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                return new ChannelSendResult(subscription, SendOutcome.Expired, error);

            return new ChannelSendResult(subscription, SendOutcome.Failed, error);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "WebPush delivery failed");
            return new ChannelSendResult(subscription, SendOutcome.Failed, ex.Message);
        }
        finally
        {
            if (ownsClient)
                client.Dispose();
        }
    }
}
