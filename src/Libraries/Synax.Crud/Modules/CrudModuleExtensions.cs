using System.Reflection;
using Synax.Crud.Modules;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;
using Synax.Modules;
using Synax.Modules.Surface;

[assembly: SynaxModuleSurface(typeof(CrudRequestSurfaceProvider))]

namespace Synax.Crud.Modules;

/// <summary>
/// Synax.Crud's contribution to <see cref="ISynaxModuleBuilder"/>.
/// </summary>
public static class CrudModuleExtensions
{
    /// <summary>The module capability covering CRUD request types.</summary>
    public static readonly SynaxModuleCapability CrudRequests =
        new("Crud", "CRUD request", "AddCrud<TContext>()");

    /// <summary>
    /// Scans the module's assemblies for <c>Crud*Request</c> subclasses and registers a generic
    /// handler for each. Pass <paramref name="assemblies"/> to scan a subset instead.
    /// <para>
    /// Registration is held back until every module has registered, because a generic handler
    /// yields to a handwritten one for the same request — including a handwritten handler that
    /// lives in another module.
    /// </para>
    /// </summary>
    public static ISynaxModuleBuilder AddCrud<TContext>(
        this ISynaxModuleBuilder builder, params Assembly[] assemblies)
        where TContext : ISynaxContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.DeferLast(services =>
        {
            var targets = assemblies.Length > 0 ? assemblies : [.. builder.Assemblies];

            services.AddSynaxMediator(options => options.AddSynaxCrud<TContext>(targets));

            foreach (var assembly in targets)
                builder.Claim(CrudRequests, assembly);
        });
    }
}

/// <summary>Reports the CRUD requests a module assembly contains.</summary>
internal sealed class CrudRequestSurfaceProvider : SynaxModuleSurfaceProvider
{
    public override SynaxModuleCapability Capability => CrudModuleExtensions.CrudRequests;

    protected override bool IsSurfaceType(Type type)
        => DependencyInjectionExtensions.FindCrudBase(type) is not null;
}
