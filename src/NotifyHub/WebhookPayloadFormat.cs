namespace NotifyHub;

/// <summary>
/// JSON body shape used when posting to a webhook target (<see cref="Channels.WebhookChannel"/>).
/// Some third-party receivers expect a specific shape rather than NotifyHub's generic
/// <c>{ title, body, url, data }</c> payload - e.g. Slack's incoming webhooks require a top-level
/// <c>text</c> field and reject requests without one; Discord requires <c>content</c> (or
/// <c>embeds</c>). Picking the matching format is required for real delivery to those services -
/// the generic shape alone does not satisfy either of them.
/// </summary>
public enum WebhookPayloadFormat
{
    /// <summary>NotifyHub's own generic shape: <c>{ title, body, url, data }</c>. Works for any
    /// receiver that reads its own JSON (Home Assistant, n8n, your own endpoints). Default.</summary>
    Generic,

    /// <summary>Slack incoming webhook shape: <c>{ text }</c> (title/body/url combined into one
    /// message string).</summary>
    Slack,

    /// <summary>Discord webhook shape: <c>{ content }</c> (title/body/url combined into one
    /// message string).</summary>
    Discord,
}
