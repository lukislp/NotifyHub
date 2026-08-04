namespace NotifyHub.AspNetCore;

/// <summary>A subscription registered via the HTTP endpoints, associated with a host app user ID
/// string (opaque - any format, e.g. your own user ID or device ID).</summary>
public sealed record StoredSubscription(string Id, string UserId, Subscription Subscription, DateTimeOffset CreatedAt);

/// <summary>
/// Storage for the subscriptions registered via <see cref="NotifyHubEndpoints"/>. The host app
/// can plug in its own implementation (against its existing DB) via
/// <see cref="NotifyHubEndpointsBuilder.WithSubscriptionStore"/> - without that, the default
/// <see cref="InMemorySubscriptionStore"/> is used (does not survive a restart, only meant for
/// trying things out).
/// </summary>
public interface ISubscriptionStore
{
    Task<StoredSubscription> UpsertAsync(string userId, Subscription subscription, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<StoredSubscription>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<StoredSubscription>> GetAllAsync(CancellationToken ct = default);
}
