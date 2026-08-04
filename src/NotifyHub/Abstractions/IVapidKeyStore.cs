namespace NotifyHub.Abstractions;

/// <summary>A once-generated VAPID key pair along with its contact subject (mailto:/https:).</summary>
public sealed record VapidKeys(string Subject, string PublicKey, string PrivateKey);

/// <summary>
/// Persists the VAPID key pair generated once, automatically. The host app implements this
/// against its own storage (a DB row, a JSON file, etc.) - the library itself never writes
/// files or runs a database.
/// </summary>
public interface IVapidKeyStore
{
    Task<VapidKeys?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(VapidKeys keys, CancellationToken ct = default);
}
