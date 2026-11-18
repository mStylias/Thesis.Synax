using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.ExceptionHandling;
using Synax.AspNetCore.HealthChecks;
using Synax.AspNetCore.Identity;
using Synax.AspNetCore.Modules;
using Synax.Modules;
using Synax.Modules.Surface;

[assembly: SynaxModuleSurface(typeof(EndpointSurfaceProvider))]
[assembly: SynaxModuleSurface(typeof(HealthCheckSurfaceProvider))]

namespace Synax.AspNetCore.Modules;

/// <summary>
/// Synax.AspNetCore's contribution to <see cref="ISynaxModuleBuilder"/>: a module owns its
/// endpoints, health checks and exception mappings rather than the host listing them all.
/// </summary>
public static class AspNetCoreModuleExtensions
{
    /// <summary>The module capability covering <see cref="IEndpoint"/> implementations.</summary>
    public static readonly SynaxModuleCapability Endpoints =
        new("Endpoints", "endpoint", "AddEndpoints()");

    /// <summary>The module capability covering <see cref="ISynaxHealthCheck"/> implementations.</summary>
    public static readonly SynaxModuleCapability HealthChecks =
        new("HealthChecks", "health check", "AddHealthChecks()");

    /// <summary>
    /// Scans the module's assemblies for endpoints. Pass <paramref name="assemblies"/> to scan a
    /// subset instead.
    /// <para>
    /// Registering endpoints also arms a startup check: if <c>app.MapSynaxEndpoints()</c> is never
    /// called the host fails to start, rather than serving 404s for every route the module owns.
    /// </para>
    /// </summary>
    public static ISynaxModuleBuilder AddEndpoints(
        this ISynaxModuleBuilder builder, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services =>
        {
            var targets = assemblies.Length > 0 ? assemblies : [.. builder.Assemblies];

            services.AddSynaxEndpoints(targets);

            foreach (var assembly in targets)
                builder.Claim(Endpoints, assembly);

            if (builder.ValidationEnabled)
                ArmMappingGuard(services);
        });
    }

    /// <summary>
    /// Scans the module's assemblies for <see cref="ISynaxHealthCheck"/> implementations and
    /// registers them. Pass <paramref name="assemblies"/> to scan a subset instead.
    /// </summary>
    public static ISynaxModuleBuilder AddHealthChecks(
        this ISynaxModuleBuilder builder, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services =>
        {
            var targets = assemblies.Length > 0 ? assemblies : [.. builder.Assemblies];

            var checkTypes = targets
                .SelectMany(SynaxModuleSurfaceProvider.GetLoadableTypes)
                .Where(HealthCheckSurfaceProvider.IsHealthCheck)
                .ToArray();

            services.AddSynaxHealthChecks(options =>
            {
                foreach (var checkType in checkTypes)
                    options.AddCheck(checkType);
            });

            foreach (var assembly in targets)
                builder.Claim(HealthChecks, assembly);
        });
    }

    /// <summary>
    /// Registers named health checks rather than scanning. Only the listed checks are claimed, so
    /// an implementation the module forgot is still reported by surface validation.
    /// </summary>
    public static ISynaxModuleBuilder AddHealthChecks(
        this ISynaxModuleBuilder builder, Action<SynaxHealthCheckOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.Defer(services =>
        {
            var options = new SynaxHealthCheckOptions();
            configure(options);
            var checkTypes = options.HealthCheckTypes.ToArray();

            services.AddSynaxHealthChecks(scoped =>
            {
                foreach (var checkType in checkTypes)
                    scoped.AddCheck(checkType);
            });

            builder.Claim(HealthChecks, checkTypes);
        });
    }

    /// <summary>
    /// Registers the Synax exception handler and, optionally, this module's exception mappings.
    /// Mappings from several modules accumulate. Activate the handler with
    /// <c>app.UseExceptionHandler()</c>.
    /// </summary>
    public static ISynaxModuleBuilder AddExceptionHandling(
        this ISynaxModuleBuilder builder, Action<SynaxExceptionHandlerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services => services.AddSynaxExceptionHandler(configure ?? (_ => { })));
    }

    /// <summary>
    /// Registers <see cref="ICurrentUser"/> and <see cref="ICurrentUser{TKey}"/> over the ambient
    /// principal. Synax consumes identity; the host still configures authentication.
    /// </summary>
    public static ISynaxModuleBuilder AddCurrentUser<TKey>(
        this ISynaxModuleBuilder builder, Action<CurrentUserOptions>? configure = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services => services.AddSynaxCurrentUser<TKey>(configure));
    }

    /// <summary>
    /// The probe is registered as an instance so this call can set <c>Expected</c> without a built
    /// provider, and <c>MapSynaxEndpoints</c> later observes the same object.
    /// </summary>
    private static void ArmMappingGuard(IServiceCollection services)
    {
        var probe = services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(EndpointMappingProbe))
            ?.ImplementationInstance as EndpointMappingProbe;

        if (probe is null)
        {
            probe = new EndpointMappingProbe();
            services.AddSingleton(probe);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EndpointMappingGuard>());
        }

        // A module may reference the library without declaring endpoints of its own; only arm the
        // guard once there is something to map.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IEndpoint)))
            probe.Expected = true;
    }
}

/// <summary>Reports the endpoints a module assembly contains.</summary>
internal sealed class EndpointSurfaceProvider : SynaxModuleSurfaceProvider
{
    public override SynaxModuleCapability Capability => AspNetCoreModuleExtensions.Endpoints;

    protected override bool IsSurfaceType(Type type) => typeof(IEndpoint).IsAssignableFrom(type);
}

/// <summary>Reports the health checks a module assembly contains.</summary>
internal sealed class HealthCheckSurfaceProvider : SynaxModuleSurfaceProvider
{
    public override SynaxModuleCapability Capability => AspNetCoreModuleExtensions.HealthChecks;

    protected override bool IsSurfaceType(Type type) => IsHealthCheck(type);

    internal static bool IsHealthCheck(Type type)
        => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
           && typeof(ISynaxHealthCheck).IsAssignableFrom(type);
}
