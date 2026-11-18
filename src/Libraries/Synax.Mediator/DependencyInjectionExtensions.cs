using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.Internal;

namespace Synax.Mediator;

/// <summary>
/// Extension methods for registering Synax.Mediator services with the DI container.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers the mediator and all handlers discovered via the <paramref name="configure"/> callback.
    /// </summary>
    public static IServiceCollection AddSynaxMediator(this IServiceCollection services, Action<MediatorOptions> configure)
    {
        var options = new MediatorOptions();
        configure(options);

        return RegisterCore(services, options);
    }

    /// <summary>
    /// Registers the mediator and all handlers found in the specified <paramref name="assemblies"/> using default options.
    /// </summary>
    public static IServiceCollection AddSynaxMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        var options = new MediatorOptions();
        foreach (var assembly in assemblies)
            options.AddHandlersFromAssembly(assembly);

        return RegisterCore(services, options);
    }

    private static IServiceCollection RegisterCore(IServiceCollection services, MediatorOptions options)
    {
        // A second AddSynaxMediator call merges into the existing registry so handlers from
        // earlier calls stay reachable; the first call's options define lifetime-independent
        // defaults (e.g. NotificationPublishStrategy).
        var existingRegistry = services
            .FirstOrDefault(sd => sd.ServiceType == typeof(HandlerRegistry))
            ?.ImplementationInstance as HandlerRegistry;

        var registry = existingRegistry ?? new HandlerRegistry();

        // Behaviors accumulate on the shared registry instead of staying scoped to this call,
        // so a module that registers a behavior and a module that registers a handler compose
        // in either order. Re-registering the same behavior type widens its scope rather than
        // attaching it to the pipeline twice.
        foreach (var behavior in options.GlobalBehaviors)
            registry.AddGlobalBehavior(behavior);

        var registrar = new HandlerRegistrar(services, options.HandlerLifetime, registry);

        foreach (var assembly in options.Assemblies)
        {
            if (registry.TryMarkAssemblyScanned(assembly))
                registrar.RegisterHandlers(assembly);
        }

        // Explicit handlers register after the scan so a handwritten handler for the same
        // request wins — an explicitly-added default (e.g. a Synax.Crud generic handler)
        // is skipped instead of raising a duplicate-handler error.
        foreach (var handlerType in options.ExplicitHandlerTypes)
            registrar.RegisterHandlerType(handlerType, skipIfRequestHandled: true);

        // Every call rebuilds every pipeline: handlers discovered earlier pick up the behaviors
        // this call added, and this call's handlers pick up the behaviors added before it.
        registrar.BuildPipelines();

        // Registered outside the first-call guard so a merge call can still supply log options.
        if (options.ResultLogOptions is not null)
            services.AddSingleton(options.ResultLogOptions);

        if (existingRegistry is null)
        {
            services.AddSingleton(options);
            services.AddSingleton(registry);
            services.AddScoped<IMediator, Internal.Mediator>();
            services.AddScoped<ISender>(sp => sp.GetRequiredService<IMediator>());
            services.AddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());
        }

        return services;
    }
}
