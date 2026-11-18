using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synax.Modules.Internal;

namespace Synax.Modules;

/// <summary>
/// Registers <see cref="ISynaxModule"/> implementations: each module declares its surface once, and
/// this call performs every underlying Synax registration and then verifies that nothing in the
/// module's assemblies was left unregistered.
/// <para>
/// Calls merge — the module registry lives on the service collection, so several calls compose and
/// a module listed twice is configured once.
/// </para>
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>Registers every module found in <paramref name="assemblies"/>.</summary>
    public static IServiceCollection AddSynaxModules(
        this IServiceCollection services, params Assembly[] assemblies)
        => services.AddSynaxModules(configuration: null, assemblies);

    /// <summary>
    /// Registers every module found in <paramref name="assemblies"/>, giving each one access to
    /// <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddSynaxModules(
        this IServiceCollection services, IConfiguration? configuration, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return services.AddSynaxModules(options =>
        {
            options.Configuration = configuration;

            foreach (var assembly in assemblies)
                options.AddModulesFromAssembly(assembly);
        });
    }

    /// <summary>Registers the modules configured by <paramref name="configure"/>.</summary>
    public static IServiceCollection AddSynaxModules(
        this IServiceCollection services, Action<SynaxModuleOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SynaxModuleOptions();
        configure(options);

        var registry = GetOrAddRegistry(services);
        var pending = new List<ModuleRecord>();
        var queue = new Queue<ISynaxModule>(options.ResolveModules());

        // Breadth-first, because DependsOn is only known once a module has been configured. The
        // TryAdd guard makes this cycle-safe and keeps a module already registered by an earlier
        // call from being configured twice.
        while (queue.Count > 0)
        {
            var module = queue.Dequeue();

            if (registry.TryAdd(module.GetType(), module.Name) is not { } record)
                continue;

            record.AddAssembly(module.GetType().Assembly);

            var builder = new SynaxModuleBuilder(
                registry, record, services, options.Configuration, options.ValidateSurface);

            module.Configure(builder);
            pending.Add(record);

            foreach (var dependency in record.Dependencies)
                queue.Enqueue(ModuleActivator.Create(dependency));
        }

        // Capability registration is deferred so it observes each module's complete assembly list,
        // whatever order Configure called things in. The second pass is for registrations that must
        // see every other module first — generic CRUD handlers yielding to handwritten ones.
        foreach (var registration in pending.SelectMany(record => record.Deferred))
            registration(services);

        foreach (var registration in pending.SelectMany(record => record.DeferredLast))
            registration(services);

        if (options.ValidateSurface)
            ModuleSurfaceValidator.Validate(registry);

        return services;
    }

    private static ModuleRegistry GetOrAddRegistry(IServiceCollection services)
    {
        var existing = services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(ModuleRegistry))
            ?.ImplementationInstance as ModuleRegistry;

        if (existing is not null)
            return existing;

        var registry = new ModuleRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
