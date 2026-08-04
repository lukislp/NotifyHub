using NotifyHub.Channels;
using NotifyHub.Options;
using Xunit;

namespace NotifyHub.Tests;

public class EmailChannelTests
{
    [Fact]
    public void Enabled_IsFalse_WithoutOptions()
    {
        Assert.False(new EmailChannel(null).Enabled);
    }

    [Fact]
    public void Enabled_IsTrue_WithOptions()
    {
        var options = new SmtpOptions { Host = "smtp.example.com", FromAddress = "noreply@example.com" };
        Assert.True(new EmailChannel(options).Enabled);
    }

    [Fact]
    public async Task SendAsync_ReturnsSkipped_WhenDisabled()
    {
        var channel = new EmailChannel(null);
        var result = await channel.SendAsync(Subscription.Email("user@example.com"), new NotificationMessage { Title = "T", Body = "B" });
        Assert.Equal(SendOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenEmailAddressMissing()
    {
        var options = new SmtpOptions { Host = "smtp.example.com", FromAddress = "noreply@example.com" };
        var channel = new EmailChannel(options);
        var wrongSubscription = Subscription.Apns("tok"); // not an email channel -> EmailAddress missing

        await Assert.ThrowsAsync<ArgumentException>(() =>
            channel.SendAsync(wrongSubscription, new NotificationMessage { Title = "T", Body = "B" }));
    }
}
