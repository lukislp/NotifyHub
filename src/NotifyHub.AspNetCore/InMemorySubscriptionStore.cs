using System.Collections.Concurrent;

namespace NotifyHub.AspNetCore;

/// <summary>Default <see cref="ISubscriptionStore"/> - does not survive a restart, meant for the
/// demo/trying things out. Production host apps implement the interface against their own
/// storage.</summary>
public sealed class InMemorySubscriptionStore : ISubscriptionStore
{
    private readonly ConcurrentDictionary<string, StoredSubscription> _byId = new();

    public Task<StoredSubscription> UpsertAsync(string userId, Subscription subscription, CancellationToken ct = default)
    {
        var naturalKey = NaturalKey(subscription);
        var existing = _byId.Values.FirstOrDefault(s => s.UserId == userId && NaturalKey(s.Subscription) == naturalKey);

        var stored = existing is null
            ? new StoredSubscription(Guid.NewGuid().ToString("N"), userId, subscription, DateTimeOffset.UtcNow)
            : existing with { Subscription = subscription };

        _byId[stored.Id] = stored;
        return Task.FromResult(stored);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _byId.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredSubscription>> GetByUserIdAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StoredSubscription>>(_byId.Values.Where(s => s.UserId == userId).ToList());

    public Task<IReadOnlyList<StoredSubscription>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StoredSubscription>>(_byId.Values.ToList());

    private static string NaturalKey(Subscription s) => s.Channel switch
    {
        NotificationChannel.WebPush => $"webpush:{s.Endpoint}",
        NotificationChannel.Apns => $"apns:{s.DeviceToken}",
        NotificationChannel.Fcm => $"fcm:{s.DeviceToken}",
        NotificationChannel.Webhook => $"webhook:{s.Url}",
        NotificationChannel.Email => $"email:{s.EmailAddress}",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}
