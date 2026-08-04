namespace NotifyHub;

/// <summary>Channel-independent content of a notification.</summary>
public sealed record NotificationMessage
{
    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>Optional target URL to open when the notification is tapped.</summary>
    public string? Url { get; init; }

    /// <summary>Additional, channel-specific payload data (e.g. for deep links).</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }
}
