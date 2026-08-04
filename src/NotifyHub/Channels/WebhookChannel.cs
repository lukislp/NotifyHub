using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyHub.Abstractions;

namespace NotifyHub.Channels;

/// <summary>
/// Generic delivery channel: POSTs the notification JSON to an arbitrary URL. Covers virtually
/// any system that can accept a webhook (Slack/Discord/Home Assistant/n8n/your own endpoints) -
/// always active, since no external credentials are required.
/// </summary>
public sealed class WebhookChannel(HttpClient? httpClient = null, ILogger<WebhookChannel>? logger = null) : INotificationChannel
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();

    public NotificationChannel Channel => NotificationChannel.Webhook;
    public bool Enabled => true;

    public async Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default)
    {
        if (subscription.Url is null)
            throw new ArgumentException("Webhook subscription requires Url.", nameof(subscription));

        var payload = JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            url = message.Url,
            data = message.Data,
        });

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(subscription.Url, content, ct);
            if (response.IsSuccessStatusCode)
                return new ChannelSendResult(subscription, SendOutcome.Delivered);

            // 404/410 = target endpoint no longer exists - treat like other channels as
            // "expired" so the host app can clean up the subscription.
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
                return new ChannelSendResult(subscription, SendOutcome.Expired, $"HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(ct);
            logger?.LogWarning("Webhook delivery failed: HTTP {Status} {Body}", (int)response.StatusCode, body);
            return new ChannelSendResult(subscription, SendOutcome.Failed, $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Webhook delivery failed (network/protocol)");
            return new ChannelSendResult(subscription, SendOutcome.Failed, ex.Message);
        }
    }
}
