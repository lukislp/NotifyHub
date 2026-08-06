using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyHub.Abstractions;

namespace NotifyHub.Channels;

/// <summary>
/// Generic delivery channel: POSTs the notification to an arbitrary URL. Covers virtually
/// any system that can accept a webhook (Slack/Discord/Home Assistant/n8n/your own endpoints) -
/// always active, since no external credentials are required.
///
/// Three opt-in extension points on <see cref="Subscription"/>, all off by default (zero required
/// configuration, unchanged behavior if unused):
/// <list type="bullet">
/// <item><see cref="Subscription.WebhookFormat"/> - the JSON body shape. Defaults to NotifyHub's
/// own generic shape (<c>{ title, body, url, data }</c>), which Home Assistant/n8n/your own
/// endpoints can read directly. Slack and Discord expect their own shape instead
/// (<c>{ text }</c> / <c>{ content }</c>) and reject the generic one - pick
/// <see cref="WebhookPayloadFormat.Slack"/>/<see cref="WebhookPayloadFormat.Discord"/> for
/// those.</item>
/// <item><see cref="Subscription.WebhookSecret"/> - adds an
/// <c>X-NotifyHub-Signature: sha256=&lt;hex&gt;</c> HMAC header (over the raw request body) so
/// the receiver can verify the call actually came from NotifyHub.</item>
/// <item><see cref="Subscription.WebhookHeaders"/> - arbitrary extra headers (e.g. an
/// <c>Authorization</c> token expected by a custom endpoint).</item>
/// </list>
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

        var payload = BuildPayload(message, subscription.WebhookFormat);

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url) { Content = content };

            if (subscription.WebhookSecret is not null)
            {
                var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(subscription.WebhookSecret), Encoding.UTF8.GetBytes(payload)));
                request.Headers.TryAddWithoutValidation("X-NotifyHub-Signature", $"sha256={signature}");
            }

            if (subscription.WebhookHeaders is not null)
            {
                foreach (var (key, value) in subscription.WebhookHeaders)
                    request.Headers.TryAddWithoutValidation(key, value);
            }

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return new ChannelSendResult(subscription, SendOutcome.Delivered);

            // 404/410 = target endpoint no longer exists - treat like other channels as
            // "expired" so the host app can clean up the subscription.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
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

    private static string BuildPayload(NotificationMessage message, WebhookPayloadFormat format) => format switch
    {
        WebhookPayloadFormat.Slack => JsonSerializer.Serialize(new { text = FormatText(message, "*") }),
        WebhookPayloadFormat.Discord => JsonSerializer.Serialize(new { content = FormatText(message, "**") }),
        _ => JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            url = message.Url,
            data = message.Data,
            image = message.ImageUrl,
            badge = message.Badge,
            sound = message.Sound,
            silent = message.Silent,
        }),
    };

    /// <summary>Combines title/body/url into one message string with the given emphasis markup
    /// (Slack: <c>*bold*</c>, Discord: <c>**bold**</c>) around the title, since neither service's
    /// simple webhook shape has separate title/body fields.</summary>
    private static string FormatText(NotificationMessage message, string emphasis)
    {
        var text = $"{emphasis}{message.Title}{emphasis}\n{message.Body}";
        return message.Url is null ? text : $"{text}\n{message.Url}";
    }
}

