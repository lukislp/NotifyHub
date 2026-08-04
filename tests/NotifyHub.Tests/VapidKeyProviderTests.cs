using Xunit;

namespace NotifyHub.Tests;

public class VapidKeyProviderTests
{
    [Fact]
    public async Task EnsureKeysAsync_GeneratesOnce_AndPersists()
    {
        var store = new InMemoryVapidKeyStore();
        var provider = new VapidKeyProvider(store, "mailto:test@example.com");

        var first = await provider.EnsureKeysAsync();
        var second = await provider.EnsureKeysAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, store.SaveCallCount);
        Assert.NotEmpty(first.PublicKey);
        Assert.NotEmpty(first.PrivateKey);
        Assert.Equal("mailto:test@example.com", first.Subject);
    }

    [Fact]
    public async Task EnsureKeysAsync_UsesExistingKeys_WhenStoreAlreadyHasThem()
    {
        var store = new InMemoryVapidKeyStore();
        var existing = new Abstractions.VapidKeys("mailto:existing@example.com", "pub", "priv");
        await store.SaveAsync(existing);

        var provider = new VapidKeyProvider(store, "mailto:ignored@example.com");
        var keys = await provider.EnsureKeysAsync();

        Assert.Equal(existing, keys);
        // No extra Save call from the provider - only the setup call above.
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public async Task EnsureKeysAsync_IsThreadSafe_ConcurrentCallsGenerateOnlyOnce()
    {
        var store = new InMemoryVapidKeyStore();
        var provider = new VapidKeyProvider(store, "mailto:test@example.com");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => provider.EnsureKeysAsync()));

        Assert.All(results, r => Assert.Equal(results[0], r));
        Assert.Equal(1, store.SaveCallCount);
    }
}
