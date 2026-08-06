using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NotifyHub.Abstractions;
using NotifyHub.Options;

namespace NotifyHub.Channels;

/// <summary>
/// Firebase Cloud Messaging (Android, as well as generic FCM tokens for iOS/web via Firebase) -
/// HTTP v1 API with Google OAuth2 via a self-signed JWT from the service account JSON.
/// Deliberately without the Firebase Admin SDK, to avoid forcing a heavy dependency - the same
/// lean style as <see cref="ApnsChannel"/>. A silent no-op without <see cref="FcmOptions"/>.
/// </summary>
public sealed class FcmChannel : INotificationChannel
{
    private const string TokenScope = "https://www.googleapis.com/auth/firebase.messaging";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50);

    private readonly FcmOptions? _options;
    private readonly HttpClient _http;
    private readonly ILogger<FcmChannel>? _logger;
    private readonly ServiceAccount? _serviceAccount;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedAccessToken;
    private DateTime _cachedAccessTokenCreatedAt;

    public FcmChannel(FcmOptions? options, HttpClient? httpClient = null, ILogger<FcmChannel>? logger = null)
    {
        _options = options;
        _http = httpClient ?? new HttpClient();
        _logger = logger;
        _serviceAccount = options is null ? null : JsonSerializer.Deserialize<ServiceAccount>(options.ServiceAccountJson);
    }

    public NotificationChannel Channel => NotificationChannel.Fcm;
    public bool Enabled => _options is not null && _serviceAccount is not null;

    public async Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default)
    {
        if (!Enabled)
            return new ChannelSendResult(subscription, SendOutcome.Skipped);
        if (subscription.DeviceToken is null)
            throw new ArgumentException("FCM subscription requires DeviceToken.", nameof(subscription));

        var options = _options!;
        // A silent/data-only message omits "notification" entirely per FCM's convention - the
        // app receives only "data" and decides itself whether/how to surface anything.
        object? notification = message.Silent
            ? null
            : new { title = message.Title, body = message.Body, image = message.ImageUrl };

        var messageFields = new Dictionary<string, object?>
        {
            ["token"] = subscription.DeviceToken,
            ["data"] = message.Data,
        };
        if (notification is not null)
            messageFields["notification"] = notification;

        var body = new { message = messageFields };

        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{options.ProjectId}/messages:send")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return new ChannelSendResult(subscription, SendOutcome.Delivered);

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            // UNREGISTERED/NOT_FOUND = token became invalid (app uninstalled, etc.).
            if (response.StatusCode == HttpStatusCode.NotFound
                || responseBody.Contains("UNREGISTERED")
                || responseBody.Contains("NOT_FOUND"))
                return new ChannelSendResult(subscription, SendOutcome.Expired, responseBody);

            _logger?.LogWarning("FCM delivery failed: HTTP {Status} {Body}", (int)response.StatusCode, responseBody);
            return new ChannelSendResult(subscription, SendOutcome.Failed, responseBody);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "FCM delivery failed (network/protocol)");
            return new ChannelSendResult(subscription, SendOutcome.Failed, ex.Message);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedAccessToken is not null && DateTime.UtcNow - _cachedAccessTokenCreatedAt < TokenLifetime)
            return _cachedAccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedAccessToken is not null && DateTime.UtcNow - _cachedAccessTokenCreatedAt < TokenLifetime)
                return _cachedAccessToken;

            var account = _serviceAccount!;
            var tokenUri = string.IsNullOrEmpty(account.TokenUri) ? "https://oauth2.googleapis.com/token" : account.TokenUri;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = account.ClientEmail,
                scope = TokenScope,
                aud = tokenUri,
                iat = now,
                exp = now + 3600,
            }));
            var signingInput = Encoding.ASCII.GetBytes($"{header}.{claims}");

            using var rsa = RSA.Create();
            rsa.ImportFromPem(account.PrivateKey);
            var signature = Base64Url(rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var assertion = $"{header}.{claims}.{signature}";

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion,
                }),
            };
            using var tokenResponse = await _http.SendAsync(tokenRequest, ct);
            tokenResponse.EnsureSuccessStatusCode();
            var tokenResponseBody = await tokenResponse.Content.ReadAsStringAsync(ct);
            var tokenJson = JsonSerializer.Deserialize<TokenResponse>(tokenResponseBody)
                ?? throw new InvalidOperationException("Google OAuth2 response was empty.");

            _cachedAccessToken = tokenJson.AccessToken;
            _cachedAccessTokenCreatedAt = DateTime.UtcNow;
            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record ServiceAccount
    {
        [JsonPropertyName("client_email")] public required string ClientEmail { get; init; }
        [JsonPropertyName("private_key")] public required string PrivateKey { get; init; }
        [JsonPropertyName("token_uri")] public string? TokenUri { get; init; }
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")] public required string AccessToken { get; init; }
    }
}
