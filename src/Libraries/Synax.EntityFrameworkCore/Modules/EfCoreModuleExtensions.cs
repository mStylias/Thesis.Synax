using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Modules;
using Synax.Modules;
using Synax.Modules.Surface;

[assembly: SynaxModuleSurface(typeof(EntitySurfaceProvider))]

namespace Synax.EntityFrameworkCore.Modules;

/// <summary>
/// Synax.EntityFrameworkCore's contribution to <see cref="ISynaxModuleBuilder"/>. The context tag
/// was already the module boundary for persistence — <c>IUnitOfWork&lt;TContext&gt;</c>,
/// <c>IRepository&lt;TEntity, TContext&gt;</c> — and this is where a module states which tag and
/// <c>DbContext</c> are its own.
/// </summary>
public static class EfCoreModuleExtensions
{
    /// <summary>The module capability covering <see cref="IEntity"/> implementations.</summary>
    public static readonly SynaxModuleCapability Entities =
        new("Entities", "entity", "AddPersistence<TDbContext, TContext>()");

    /// <summary>
    /// Registers the unit of work, database manager and repositories for a <c>DbContext</c> that
    /// acts as its own context tag.
    /// </summary>
    /// <inheritdoc cref="AddPersistence{TDbContext, TContext}" path="/remarks"/>
    public static ISynaxModuleBuilder AddPersistence<TDbContext>(
        this ISynaxModuleBuilder builder, Action<SynaxDbContextOptions<TDbContext>>? configure = null)
        where TDbContext : DbContext, ISynaxContext
        => builder.AddPersistence<TDbContext, TDbContext>(configure);

    /// <summary>
    /// Registers the unit of work, database manager and repositories for
    /// <typeparamref name="TDbContext"/>, resolved under the standalone tag
    /// <typeparamref name="TContext"/>.
    /// </summary>
    /// <remarks>
    /// Without <paramref name="configure"/>, every <see cref="IEntity"/> in the module's assemblies
    /// gets a repository — a module already declares which assemblies it owns, so there is no
    /// namespace to restate. Supply <paramref name="configure"/> to scan by namespace instead; the
    /// entities it misses are then reported by surface validation rather than failing on first use.
    /// </remarks>
    public static ISynaxModuleBuilder AddPersistence<TDbContext, TContext>(
        this ISynaxModuleBuilder builder, Action<SynaxDbContextOptions<TDbContext>>? configure = null)
        where TDbContext : DbContext
        where TContext : ISynaxContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services =>
        {
            IReadOnlyCollection<Type> entityTypes = [];

            services.AddSynaxEfCore(options =>
            {
                var dbContext = options.ForDbContext<TDbContext>().As<TContext>();

                if (configure is not null)
                    configure(dbContext);
                else
                    foreach (var assembly in builder.Assemblies)
                        dbContext.ScanAssembly(assembly);

                entityTypes = dbContext.EntityTypes;
            });

            builder.Claim(Entities, entityTypes);
        });
    }
}

/// <summary>Reports the entities a module assembly contains.</summary>
internal sealed class EntitySurfaceProvider : SynaxModuleSurfaceProvider
{
    public override SynaxModuleCapability Capability => EfCoreModuleExtensions.Entities;

    protected override bool IsSurfaceType(Type type) => typeof(IEntity).IsAssignableFrom(type);
}
