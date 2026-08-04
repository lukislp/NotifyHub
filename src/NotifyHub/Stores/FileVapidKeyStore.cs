using System.Text.Json;
using NotifyHub.Abstractions;

namespace NotifyHub.Stores;

/// <summary>
/// Default storage location for the VAPID key pair: a JSON file. Used when the host app does not
/// call <see cref="NotifyHubBuilder.WithVapidKeyStore"/> itself - "works out of the box" without
/// any storage of its own, yet stays stable across restarts (unlike a purely in-memory store,
/// which would generate new keys on every restart and thereby invalidate all existing browser
/// subscriptions). For production multi-instance deployments or an existing DB, the host app
/// should implement <see cref="IVapidKeyStore"/> itself.
/// </summary>
public sealed class FileVapidKeyStore(string path) : IVapidKeyStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<VapidKeys?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        await _lock.WaitAsync(ct);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<VapidKeys>(stream, cancellationToken: ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(VapidKeys keys, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, keys, cancellationToken: ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}
