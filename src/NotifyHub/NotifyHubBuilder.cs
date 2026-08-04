using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotifyHub.Abstractions;
using NotifyHub.Channels;
using NotifyHub.Options;
using NotifyHub.Stores;

namespace NotifyHub;

/// <summary>
/// Fluent configuration for <see cref="ServiceCollectionExtensions.AddNotifyHub"/>. Every channel
/// is simply disabled (a silent no-op) without a matching <c>With...</c> call - only WebPush
/// needs no configuration (VAPID keys are generated automatically).
/// </summary>
public sealed class NotifyHubBuilder(IServiceCollection services)
{
    private string _vapidSubject = "mailto:notifyhub@localhost";
    private IVapidKeyStore? _vapidKeyStore;
    private ApnsOptions? _apnsOptions;
    private FcmOptions? _fcmOptions;
    private SmtpOptions? _smtpOptions;

    /// <summary>Contact detail in the VAPID JWT (mailto: or https:), not shown by browsers but
    /// part of the protocol. Default: "mailto:notifyhub@localhost" - override this with a real,
    /// resolvable domain before shipping: Apple's web push service rejects subjects on
    /// non-existent domains (".local", "localhost", ...) with "403 BadJwtToken". Chrome/Firefox
    /// do not validate this, so the problem only surfaces on iOS/Safari.</summary>
    public NotifyHubBuilder WithVapidSubject(string subject)
    {
        _vapidSubject = subject;
        return this;
    }

    /// <summary>Custom persistence for the VAPID key pair (e.g. your own DB). Without this call,
    /// <see cref="FileVapidKeyStore"/> is used with "notifyhub-vapid-keys.json" in the current
    /// working directory.</summary>
    public NotifyHubBuilder WithVapidKeyStore(IVapidKeyStore store)
    {
        _vapidKeyStore = store;
        return this;
    }

    public NotifyHubBuilder WithApns(ApnsOptions options)
    {
        _apnsOptions = options;
        return this;
    }

    public NotifyHubBuilder WithFcm(FcmOptions options)
    {
        _fcmOptions = options;
        return this;
    }

    public NotifyHubBuilder WithSmtp(SmtpOptions options)
    {
        _smtpOptions = options;
        return this;
    }

    internal void Build()
    {
        var vapidKeyStore = _vapidKeyStore ?? new FileVapidKeyStore("notifyhub-vapid-keys.json");
        services.AddSingleton(vapidKeyStore);
        services.AddSingleton<IVapidKeyStore>(vapidKeyStore);

        var vapidSubject = _vapidSubject;
        services.AddSingleton(sp => new VapidKeyProvider(sp.GetRequiredService<IVapidKeyStore>(), vapidSubject));

        services.AddSingleton<INotificationChannel>(sp =>
            new WebPushChannel(sp.GetRequiredService<VapidKeyProvider>(), CreateHttpClient(sp), sp.GetService<ILogger<WebPushChannel>>()));

        var apnsOptions = _apnsOptions;
        services.AddSingleton<INotificationChannel>(sp =>
            new ApnsChannel(apnsOptions, CreateHttpClient(sp), sp.GetService<ILogger<ApnsChannel>>()));

        var fcmOptions = _fcmOptions;
        services.AddSingleton<INotificationChannel>(sp =>
            new FcmChannel(fcmOptions, CreateHttpClient(sp), sp.GetService<ILogger<FcmChannel>>()));

        services.AddSingleton<INotificationChannel>(sp =>
            new WebhookChannel(CreateHttpClient(sp), sp.GetService<ILogger<WebhookChannel>>()));

        var smtpOptions = _smtpOptions;
        services.AddSingleton<INotificationChannel>(sp =>
            new EmailChannel(smtpOptions, sp.GetService<ILogger<EmailChannel>>()));

        services.AddSingleton<NotificationSender>();
    }

    private static HttpClient CreateHttpClient(IServiceProvider sp) =>
        sp.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
}
