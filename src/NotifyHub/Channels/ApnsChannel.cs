using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyHub.Abstractions;
using NotifyHub.Options;

namespace NotifyHub.Channels;

/// <summary>
/// Apple Push Notifications - token-based auth with a p8 key (JWT ES256), delivered over HTTP/2
/// to api.push.apple.com or api.sandbox.push.apple.com. Without <see cref="ApnsOptions"/> this
/// channel is a silent no-op (<see cref="SendOutcome.Skipped"/>) - other channels keep running
/// unaffected.
/// </summary>
public sealed class ApnsChannel : INotificationChannel
{
    private const string ProductionEndpoint = "https://api.push.apple.com";
    private const string SandboxEndpoint = "https://api.sandbox.push.apple.com";
    private static readonly TimeSpan JwtLifetime = TimeSpan.FromMinutes(45);

    private readonly ApnsOptions? _options;
    private readonly HttpClient _http;
    private readonly ILogger<ApnsChannel>? _logger;

    private readonly object _jwtLock = new();
    private string? _cachedJwt;
    private DateTime _cachedJwtCreatedAt;

    public ApnsChannel(ApnsOptions? options, HttpClient? httpClient = null, ILogger<ApnsChannel>? logger = null)
    {
        _options = options;
        _http = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.Apns;
    public bool Enabled => _options is not null;

    public async Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default)
    {
        if (!Enabled)
            return new ChannelSendResult(subscription, SendOutcome.Skipped);
        if (subscription.DeviceToken is null)
            throw new ArgumentException("APNs subscription requires DeviceToken.", nameof(subscription));

        var options = _options!;
        var endpoint = options.Endpoint ?? (options.UseSandbox ? SandboxEndpoint : ProductionEndpoint);

        var aps = new Dictionary<string, object>();
        if (message.Silent)
        {
            // Background/silent push per Apple's spec: content-available only, no alert/sound.
            aps["content-available"] = 1;
        }
        else
        {
            aps["alert"] = new { title = message.Title, body = message.Body };
            aps["sound"] = message.Sound ?? "default";
        }
        if (message.Badge is { } badge)
            aps["badge"] = badge;

        var payload = new Dictionary<string, object>{ ["aps"] = aps };
        if (message.Data is not null)
        {
            // Apple convention: custom data lives as top-level keys alongside "aps", not nested.
            foreach (var (key, value) in message.Data)
                payload[key] = value;
        }
        if (message.Url is not null)
            payload["url"] = message.Url;

        var apsJson = JsonSerializer.Serialize(payload);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/3/device/{subscription.DeviceToken}")
            {
                Version = new Version(2, 0),
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                Content = new StringContent(apsJson, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("authorization", $"bearer {GetJwt(options)}");
            request.Headers.TryAddWithoutValidation("apns-topic", options.BundleId);
            request.Headers.TryAddWithoutValidation("apns-push-type", message.Silent ? "background" : "alert");
            // Apple requires priority 5 for background/content-available pushes, 10 (immediate) for alerts.
            request.Headers.TryAddWithoutValidation("apns-priority", message.Silent ? "5" : "10");

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return new ChannelSendResult(subscription, SendOutcome.Delivered);

            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.Gone
                || body.Contains("BadDeviceToken")
                || body.Contains("DeviceTokenNotForTopic"))
                return new ChannelSendResult(subscription, SendOutcome.Expired, body);

            _logger?.LogWarning("APNs delivery failed: HTTP {Status} {Body}", (int)response.StatusCode, body);
            return new ChannelSendResult(subscription, SendOutcome.Failed, body);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "APNs delivery failed (network/protocol)");
            return new ChannelSendResult(subscription, SendOutcome.Failed, ex.Message);
        }
    }

    private string GetJwt(ApnsOptions options)
    {
        lock (_jwtLock)
        {
            if (_cachedJwt != null && DateTime.UtcNow - _cachedJwtCreatedAt < JwtLifetime)
                return _cachedJwt;

            using var key = ECDsa.Create();
            key.ImportFromPem(File.ReadAllText(options.KeyPath));

            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = options.KeyId }));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iss = options.TeamId, iat = now }));
            var signingInput = Encoding.ASCII.GetBytes($"{header}.{claims}");
            // SignData produces the IEEE P1363 format (r||s) that JWS expects for ES256.
            var signature = Base64Url(key.SignData(signingInput, HashAlgorithmName.SHA256));

            _cachedJwt = $"{header}.{claims}.{signature}";
            _cachedJwtCreatedAt = DateTime.UtcNow;
            return _cachedJwt;
        }
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
