namespace NotifyHub.Options;

/// <summary>SMTP credentials for the email channel.</summary>
public sealed record SmtpOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 587;
    public string? User { get; init; }
    public string? Password { get; init; }
    public required string FromAddress { get; init; }
    public string? FromName { get; init; }
    public bool UseSsl { get; init; } = true;
}
