using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Synax.Mapper.Core;
using Synax.Mapper.Internal;

namespace Synax.Mapper;

/// <summary>
/// Dependency injection extensions for registering AddSynaxMapper with
/// <see cref="IServiceCollection"/>.
/// <para>
/// Calls merge: every <c>AddSynaxMapper</c> on the same <see cref="IServiceCollection"/>
/// contributes its profiles to one shared configuration, which is compiled once when
/// <see cref="IMapper"/> or <see cref="MapperConfiguration"/> is first resolved. Modules can
/// therefore register their own profiles without erasing anyone else's.
/// </para>
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers <see cref="MapperConfiguration"/> and <see cref="IMapper"/> as singletons and
    /// adds the profiles configured by <paramref name="configure"/>.
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// builder.Services.AddSynaxMapper(cfg =&gt;
    /// {
    ///     cfg.AddProfile&lt;OrderProfile&gt;();
    ///     cfg.AddProfile&lt;CustomerProfile&gt;();
    /// });
    /// </code>
    /// </para>
    /// </summary>
    /// <remarks>
    /// Duplicate mappings are reported here, at registration. Errors that need the maps compiled
    /// (cyclic mappings, invalid bindings) surface when the configuration is first resolved —
    /// call <see cref="MapperConfiguration.AssertConfigurationIsValid"/> at startup to pull them
    /// forward.
    /// </remarks>
    public static IServiceCollection AddSynaxMapper(
        this IServiceCollection services,
        Action<MapperConfigurationExpression> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        GetOrAddRegistration(services).Add(configure);
        return services;
    }

    /// <summary>
    /// Scans the given assemblies for all types deriving from <see cref="MapperProfile"/>
    /// and registers them automatically. Assemblies already scanned by an earlier call are
    /// skipped, so repeating one across modules is safe.
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// builder.Services.AddSynaxMapper(typeof(OrderProfile).Assembly);
    /// </code>
    /// </para>
    /// </summary>
    /// <inheritdoc cref="AddSynaxMapper(IServiceCollection, Action{MapperConfigurationExpression})" path="/remarks"/>
    public static IServiceCollection AddSynaxMapper(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        GetOrAddRegistration(services).AddAssemblies(assemblies);
        return services;
    }

    /// <summary>
    /// Returns the accumulator shared by every call on this collection, registering it — and the
    /// deferred configuration/mapper factories — on first use.
    /// </summary>
    private static MapperRegistration GetOrAddRegistration(IServiceCollection services)
    {
        var existing = services
            .FirstOrDefault(sd => sd.ServiceType == typeof(MapperRegistration))
            ?.ImplementationInstance as MapperRegistration;

        if (existing is not null)
            return existing;

        var registration = new MapperRegistration();

        services.AddSingleton(registration);
        services.AddSingleton(_ => registration.Build());
        services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().CreateMapper());

        return registration;
    }
}
