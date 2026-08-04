using NotifyHub.Abstractions;

namespace NotifyHub.Tests;

/// <summary>Fake for tests: counts calls so that "generated only once" can be verified.</summary>
public sealed class InMemoryVapidKeyStore : IVapidKeyStore
{
    private VapidKeys? _keys;
    public int SaveCallCount { get; private set; }
    public int LoadCallCount { get; private set; }

    public Task<VapidKeys?> LoadAsync(CancellationToken ct = default)
    {
        LoadCallCount++;
        return Task.FromResult(_keys);
    }

    public Task SaveAsync(VapidKeys keys, CancellationToken ct = default)
    {
        SaveCallCount++;
        _keys = keys;
        return Task.CompletedTask;
    }
}
