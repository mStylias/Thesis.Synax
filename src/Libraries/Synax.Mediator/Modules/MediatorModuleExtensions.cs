using System.Reflection;
using Synax.Mediator.Internal;
using Synax.Mediator.Modules;
using Synax.Modules;
using Synax.Modules.Surface;
using Synax.Results.Logging;

[assembly: SynaxModuleSurface(typeof(HandlerSurfaceProvider))]

namespace Synax.Mediator.Modules;

/// <summary>
/// The mediator's contribution to <see cref="ISynaxModuleBuilder"/>: a module declares its handlers
/// and the behaviors it needs, instead of the host maintaining one central
/// <c>AddSynaxMediator</c> callback that has to know about every module.
/// </summary>
/// <remarks>
/// Global behaviors stay application-wide even when a module registers them — every registration
/// call rebuilds every pipeline, so a behavior one module adds also wraps another module's handlers.
/// Registering the same behavior from two modules widens it rather than running it twice.
/// </remarks>
public static class MediatorModuleExtensions
{
    /// <summary>The module capability covering mediator handlers.</summary>
    public static readonly SynaxModuleCapability Handlers =
        new("Handlers", "mediator handler", "AddHandlers()");

    /// <summary>
    /// Registers the mediator and scans the module's assemblies for handlers. Pass
    /// <paramref name="assemblies"/> to scan a subset instead — anything left out is then reported
    /// by surface validation.
    /// </summary>
    public static ISynaxModuleBuilder AddHandlers(
        this ISynaxModuleBuilder builder, params Assembly[] assemblies)
        => builder.AddHandlers(configure: null, assemblies);

    /// <summary>
    /// Registers the mediator and scans for handlers, with access to the underlying
    /// <see cref="MediatorOptions"/> for anything this façade does not surface.
    /// </summary>
    public static ISynaxModuleBuilder AddHandlers(
        this ISynaxModuleBuilder builder, Action<MediatorOptions>? configure, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services =>
        {
            var targets = assemblies.Length > 0 ? assemblies : [.. builder.Assemblies];

            services.AddSynaxMediator(options =>
            {
                foreach (var assembly in targets)
                    options.AddHandlersFromAssembly(assembly);

                configure?.Invoke(options);
            });

            foreach (var assembly in targets)
                builder.Claim(Handlers, assembly);
        });
    }

    /// <summary>
    /// Adds a global pipeline behavior. <paramref name="appliesTo"/> scopes it to matching request
    /// types, evaluated once at startup.
    /// </summary>
    public static ISynaxModuleBuilder AddBehavior(
        this ISynaxModuleBuilder builder, Type openGenericBehaviorType, Func<Type, bool>? appliesTo = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services => services.AddSynaxMediator(
            options => options.AddGlobalBehavior(openGenericBehaviorType, appliesTo)));
    }

    /// <summary>
    /// Logs failed and warning <c>Result</c>s returned by any handler. Application-wide, like every
    /// global behavior — one module asking for it turns it on everywhere.
    /// </summary>
    public static ISynaxModuleBuilder AddResultLogging(
        this ISynaxModuleBuilder builder, ResultLogOptions? logOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services => services.AddSynaxMediator(
            options => options.AddResultLogging(logOptions)));
    }
}

/// <summary>Reports the handler types a module assembly contains.</summary>
internal sealed class HandlerSurfaceProvider : SynaxModuleSurfaceProvider
{
    public override SynaxModuleCapability Capability => MediatorModuleExtensions.Handlers;

    protected override bool IsSurfaceType(Type type) => HandlerRegistrar.IsHandlerType(type);
}
