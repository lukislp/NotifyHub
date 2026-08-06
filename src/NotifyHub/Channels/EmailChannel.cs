using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using NotifyHub.Abstractions;
using NotifyHub.Options;

namespace NotifyHub.Channels;

/// <summary>
/// Classic universal fallback channel: SMTP email. A silent no-op without <see cref="SmtpOptions"/>.
/// Email addresses don't "expire" in the HTTP 410/Expired sense - a permanently invalid address
/// only shows up via a bounce (asynchronous, outside this send operation), so it is never
/// reported here as <see cref="SendOutcome.Expired"/>.
///
/// Uses MailKit instead of System.Net.Mail.SmtpClient, because the latter does not reliably
/// support implicit TLS (port 465) - MailKit correctly handles both STARTTLS (587/25) and
/// SSL-on-connect (465) via <see cref="SecureSocketOptions.Auto"/>.
/// </summary>
public sealed class EmailChannel(SmtpOptions? options, ILogger<EmailChannel>? logger = null) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;
    public bool Enabled => options is not null;

    public async Task<ChannelSendResult> SendAsync(Subscription subscription, NotificationMessage message, CancellationToken ct = default)
    {
        if (!Enabled)
            return new ChannelSendResult(subscription, SendOutcome.Skipped);
        if (subscription.EmailAddress is null)
            throw new ArgumentException("Email subscription requires EmailAddress.", nameof(subscription));

        var smtp = options!;
        try
        {
            var mail = new MimeMessage();
            mail.From.Add(new MailboxAddress(smtp.FromName ?? string.Empty, smtp.FromAddress));
            mail.To.Add(MailboxAddress.Parse(subscription.EmailAddress));
            mail.Subject = message.Title;

            var textBody = message.Url is null ? message.Body : $"{message.Body}\n\n{message.Url}";
            if (message.HtmlBody is not null)
            {
                // multipart/alternative: HTML for capable clients, the plain-text body as fallback.
                var builder = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = textBody };
                mail.Body = builder.ToMessageBody();
            }
            else
            {
                mail.Body = new TextPart("plain") { Text = textBody };
            }

            using var client = new SmtpClient();
            var secureOptions = smtp.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
            await client.ConnectAsync(smtp.Host, smtp.Port, secureOptions, ct);
            if (smtp.User is not null)
                await client.AuthenticateAsync(smtp.User, smtp.Password ?? string.Empty, ct);
            await client.SendAsync(mail, ct);
            await client.DisconnectAsync(true, ct);
            return new ChannelSendResult(subscription, SendOutcome.Delivered);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Email delivery failed");
            return new ChannelSendResult(subscription, SendOutcome.Failed, ex.Message);
        }
    }
}
