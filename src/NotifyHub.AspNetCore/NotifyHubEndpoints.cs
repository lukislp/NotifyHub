using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NotifyHub.AspNetCore;

public sealed record SubscribeRequest(
    string UserId,
    NotificationChannel Channel,
    string? Endpoint = null,
    string? P256dh = null,
    string? Auth = null,
    string? DeviceToken = null,
    string? Url = null,
    string? EmailAddress = null);

public sealed record SendRequest(
    string? UserId,
    bool Broadcast,
    string Title,
    string Body,
    string? Url = null,
    Dictionary<string, string>? Data = null);

public sealed record SendResultDto(string? SubscriptionId, NotificationChannel Channel, string Outcome, string? Error);

/// <summary>Ready-to-use minimal API endpoints around NotifyHub. Requires
/// <c>AddNotifyHub(...)</c> and <c>AddNotifyHubEndpoints(...)</c>.
///
/// By default, all endpoints are unauthenticated/unauthorized - just like <c>AddNotifyHub(...)</c>
/// itself, this works out of the box with zero required configuration. Since
/// <see cref="MapNotifyHubEndpoints"/> maps onto its own <see cref="RouteGroupBuilder"/>, securing
/// every mapped endpoint at once is a single extra call, without affecting the rest of the host
/// app's routes:
/// <code>
/// app.MapNotifyHubEndpoints().RequireAuthorization();
/// </code>
/// Any other <see cref="RouteGroupBuilder"/> convention (e.g. <c>RequireCors(...)</c>,
/// <c>RequireRateLimiting(...)</c>) can be chained the same way.</summary>
public static class NotifyHubEndpoints
{
    public static RouteGroupBuilder MapNotifyHubEndpoints(this IEndpointRouteBuilder app, string prefix = "/notifyhub")
    {
        var group = app.MapGroup(prefix);

        group.MapGet("/vapid-public-key", async (VapidKeyProvider vapid) =>
        {
            var keys = await vapid.EnsureKeysAsync();
            return Results.Ok(new { publicKey = keys.PublicKey });
        });

        group.MapPost("/subscriptions", async (SubscribeRequest req, ISubscriptionStore store) =>
        {
            Subscription subscription;
            try
            {
                subscription = req.Channel switch
                {
                    NotificationChannel.WebPush => Subscription.WebPush(
                        req.Endpoint ?? throw new ArgumentException("Endpoint is missing."),
                        req.P256dh ?? throw new ArgumentException("P256dh is missing."),
                        req.Auth ?? throw new ArgumentException("Auth is missing.")),
                    NotificationChannel.Apns => Subscription.Apns(req.DeviceToken ?? throw new ArgumentException("DeviceToken is missing.")),
                    NotificationChannel.Fcm => Subscription.Fcm(req.DeviceToken ?? throw new ArgumentException("DeviceToken is missing.")),
                    NotificationChannel.Webhook => Subscription.Webhook(req.Url ?? throw new ArgumentException("Url is missing.")),
                    NotificationChannel.Email => Subscription.Email(req.EmailAddress ?? throw new ArgumentException("EmailAddress is missing.")),
                    _ => throw new ArgumentOutOfRangeException(nameof(req)),
                };
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var stored = await store.UpsertAsync(req.UserId, subscription);
            return Results.Ok(new { id = stored.Id });
        });

        group.MapDelete("/subscriptions/{id}", async (string id, ISubscriptionStore store) =>
        {
            await store.DeleteAsync(id);
            return Results.NoContent();
        });

        group.MapGet("/subscriptions", async (string userId, ISubscriptionStore store) =>
        {
            var subs = await store.GetByUserIdAsync(userId);
            return Results.Ok(subs.Select(s => new { s.Id, s.Subscription.Channel, s.CreatedAt }));
        });

        group.MapPost("/notifications/send", async (SendRequest req, ISubscriptionStore store, NotificationSender sender) =>
        {
            var targets = req.Broadcast
                ? await store.GetAllAsync()
                : req.UserId is not null
                    ? await store.GetByUserIdAsync(req.UserId)
                    : [];

            if (targets.Count == 0)
                return Results.Ok(Array.Empty<SendResultDto>());

            var message = new NotificationMessage { Title = req.Title, Body = req.Body, Url = req.Url, Data = req.Data };
            var results = await sender.SendAsync(message, targets.Select(t => t.Subscription));

            // Automatically clean up expired subscriptions (pattern: HTTP 410/BadDeviceToken/UNREGISTERED).
            foreach (var (target, result) in targets.Zip(results))
            {
                if (result.Outcome == SendOutcome.Expired)
                    await store.DeleteAsync(target.Id);
            }

            var dto = targets.Zip(results, (target, result) =>
                new SendResultDto(target.Id, target.Subscription.Channel, result.Outcome.ToString(), result.Error));
            return Results.Ok(dto);
        });

        return group;
    }
}
