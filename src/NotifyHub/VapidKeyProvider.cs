using NotifyHub.Abstractions;
using NotifyHub.Channels;

namespace NotifyHub;

/// <summary>
/// Ensures exactly one VAPID key pair exists - generated automatically on the very first call
/// (<see cref="WebPushCrypto.GenerateVapidKeys"/>) and persisted via <see cref="IVapidKeyStore"/>;
/// every subsequent call returns the same pair. Manual creation/configuration of VAPID keys is
/// not supported by design.
/// </summary>
public sealed class VapidKeyProvider(IVapidKeyStore store, string subject)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private VapidKeys? _cached;

    public async Task<VapidKeys> EnsureKeysAsync(CancellationToken ct = default)
    {
        if (_cached is { } cached)
            return cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is { } cachedAfterLock)
                return cachedAfterLock;

            var existing = await store.LoadAsync(ct);
            if (existing is not null)
            {
                _cached = existing;
                return existing;
            }

            var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();
            var keys = new VapidKeys(subject, publicKey, privateKey);
            await store.SaveAsync(keys, ct);
            _cached = keys;
            return keys;
        }
        finally
        {
            _lock.Release();
        }
    }
}
