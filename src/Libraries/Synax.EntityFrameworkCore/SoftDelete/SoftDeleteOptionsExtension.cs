using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.SoftDelete;

/// <summary>
/// The options extension behind <c>AddSoftDeleteInterceptor</c>. It plugs a model-finalizing
/// convention into EF's convention set so the soft-delete query filters are applied
/// <b>automatically</b> — one registration configures the whole feature, and the classic
/// "interceptor present, filter forgotten" failure mode (soft-deleted rows silently reappearing)
/// cannot happen. It also registers the context-scoped <see cref="HardDeleteRegistry"/>.
/// </summary>
internal sealed class SoftDeleteOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.AddSingleton<IConventionSetPlugin, SoftDeleteConventionSetPlugin>();
        // Scoped in EF's internal provider = one instance per DbContext instance.
        services.AddScoped<HardDeleteRegistry>();
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Synax soft delete ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Synax:SoftDelete"] = "1";
    }
}

internal sealed class SoftDeleteConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new SoftDeleteQueryFilterConvention());
        return conventionSet;
    }
}

/// <summary>
/// Applies the soft-delete query filter to every <see cref="ISoftDelete"/> entity at model
/// finalization — after <c>OnModelCreating</c> ran, so all entities are discovered. Entities that
/// already carry the named filter (e.g. via a manual <c>ApplySoftDeleteQueryFilters()</c> call)
/// are left untouched.
/// </summary>
internal sealed class SoftDeleteQueryFilterConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType) || entityType.IsOwned())
            {
                continue;
            }

            // Query filters may only sit on the root of an inheritance hierarchy; a soft-delete
            // descendant of a non-soft-delete root would silently go unfiltered, so fail fast.
            if (entityType.BaseType is not null)
            {
                if (!typeof(ISoftDelete).IsAssignableFrom(entityType.GetRootType().ClrType))
                {
                    throw new InvalidOperationException(
                        $"Entity type '{entityType.DisplayName()}' implements ISoftDelete but is not the root " +
                        $"of its inheritance hierarchy. Query filters can only be applied to the root type " +
                        $"'{entityType.GetRootType().DisplayName()}', which must implement ISoftDelete.");
                }

                continue;
            }

            if (entityType.FindDeclaredQueryFilter(SoftDeleteModelBuilderExtensions.FilterName) is not null)
            {
                continue;
            }

            entityType.Builder.HasQueryFilter(
                SoftDeleteModelBuilderExtensions.FilterName,
                SoftDeleteModelBuilderExtensions.BuildFilter(entityType.ClrType));
        }
    }
}
