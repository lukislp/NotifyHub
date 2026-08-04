using NotifyHub.AspNetCore;
using Xunit;

namespace NotifyHub.Tests;

public class InMemorySubscriptionStoreTests
{
    [Fact]
    public async Task UpsertAsync_CreatesNewEntry_ForNewSubscription()
    {
        var store = new InMemorySubscriptionStore();
        var stored = await store.UpsertAsync("user-1", Subscription.WebPush("endpoint-a", "p", "a"));

        var all = await store.GetByUserIdAsync("user-1");
        Assert.Single(all);
        Assert.Equal(stored.Id, all[0].Id);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExisting_WhenSameUserAndNaturalKey()
    {
        var store = new InMemorySubscriptionStore();
        var first = await store.UpsertAsync("user-1", Subscription.WebPush("endpoint-a", "p-old", "a-old"));
        var second = await store.UpsertAsync("user-1", Subscription.WebPush("endpoint-a", "p-new", "a-new"));

        Assert.Equal(first.Id, second.Id); // Dedup: same record updated, not duplicated.
        var all = await store.GetByUserIdAsync("user-1");
        Assert.Single(all);
        Assert.Equal("p-new", all[0].Subscription.P256dh);
    }

    [Fact]
    public async Task UpsertAsync_CreatesSeparateEntries_ForDifferentUsers_SameEndpoint()
    {
        var store = new InMemorySubscriptionStore();
        await store.UpsertAsync("user-1", Subscription.WebPush("endpoint-a", "p", "a"));
        await store.UpsertAsync("user-2", Subscription.WebPush("endpoint-a", "p", "a"));

        Assert.Equal(2, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        var store = new InMemorySubscriptionStore();
        var stored = await store.UpsertAsync("user-1", Subscription.Webhook("https://example.com/hook"));

        await store.DeleteAsync(stored.Id);

        Assert.Empty(await store.GetByUserIdAsync("user-1"));
    }
}
