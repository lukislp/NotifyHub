using System.Collections.Concurrent;

namespace NotifyHub.Demo;

/// <summary>One received call against the demo's built-in webhook receiver.</summary>
public sealed record WebhookLogEntry(DateTimeOffset ReceivedAt, string Body, string? Signature);

/// <summary>
/// In-memory ring buffer backing the demo's built-in webhook receiver (see
/// <c>/demo/webhook-sink</c> in Program.cs) - lets the demo page show incoming webhook calls live,
/// without needing an external tool like webhook.site or a second terminal to test the
/// <see cref="NotifyHub.Channels.WebhookChannel"/>. Not persisted, sample-only.
/// </summary>
public static class WebhookLog
{
    private const int MaxEntries = 20;
    private static readonly ConcurrentQueue<WebhookLogEntry> Entries = new();

    public static void Add(string body, string? signature)
    {
        Entries.Enqueue(new WebhookLogEntry(DateTimeOffset.UtcNow, body, signature));
        while (Entries.Count > MaxEntries)
            Entries.TryDequeue(out _);
    }

    public static IReadOnlyList<WebhookLogEntry> GetAll() => Entries.Reverse().ToList();

    public static void Clear()
    {
        while (Entries.TryDequeue(out _)) { }
    }
}
