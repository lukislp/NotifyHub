using Microsoft.Extensions.DependencyInjection;

namespace NotifyHub.AspNetCore;

public sealed class NotifyHubEndpointsBuilder(IServiceCollection services)
{
    private ISubscriptionStore? _store;

    /// <summary>Custom subscription storage (e.g. against the host app's existing DB).
    /// Without this call, <see cref="InMemorySubscriptionStore"/> is used.</summary>
    public NotifyHubEndpointsBuilder WithSubscriptionStore(ISubscriptionStore store)
    {
        _store = store;
        return this;
    }

    internal void Build()
    {
        services.AddSingleton(_store ?? new InMemorySubscriptionStore());
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the dependencies for <see cref="NotifyHubEndpoints.MapNotifyHubEndpoints"/>.
    /// Requires that <c>services.AddNotifyHub(...)</c> has already been called.</summary>
    public static IServiceCollection AddNotifyHubEndpoints(this IServiceCollection services, Action<NotifyHubEndpointsBuilder>? configure = null)
    {
        var builder = new NotifyHubEndpointsBuilder(services);
        configure?.Invoke(builder);
        builder.Build();
        return services;
    }
}
