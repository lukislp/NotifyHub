using Microsoft.Extensions.DependencyInjection;

namespace NotifyHub;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="NotificationSender"/> and all channels (WebPush is always active,
    /// APNs/FCM/email only when configured in the <paramref name="configure"/> delegate). Example:
    /// <code>
    /// services.AddNotifyHub(hub => hub
    ///     .WithVapidSubject("mailto:push@myapp.com")
    ///     .WithApns(new ApnsOptions { KeyPath = "...", KeyId = "...", TeamId = "...", BundleId = "..." })
    ///     .WithFcm(FcmOptions.FromFile("service-account.json", "my-firebase-project")));
    /// </code>
    /// </summary>
    public static IServiceCollection AddNotifyHub(this IServiceCollection services, Action<NotifyHubBuilder>? configure = null)
    {
        var builder = new NotifyHubBuilder(services);
        configure?.Invoke(builder);
        builder.Build();
        return services;
    }
}
