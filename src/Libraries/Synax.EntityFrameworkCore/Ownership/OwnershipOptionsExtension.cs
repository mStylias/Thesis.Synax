using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.EntityFrameworkCore.Ownership;

/// <summary>
/// The options extension behind <c>AddOwnershipInterceptor</c>. Unlike soft delete, the ownership
/// query filter cannot be authored by a convention — its owner accessor must reference a DbContext
/// member so EF re-evaluates it per query (see <c>ApplyOwnershipQueryFilters</c>). Instead, this
/// extension plugs in a model-finalizing <b>validation</b>: if any ownership-scoped entity lacks
/// the ownership filter, model building fails loudly — turning what would be a silent cross-tenant
/// data leak into a startup error.
/// </summary>
internal sealed class OwnershipOptionsExtension<TKey> : IDbContextOptionsExtension
    where TKey : notnull
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
        => services.AddSingleton<IConventionSetPlugin, OwnershipConventionSetPlugin<TKey>>();

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"using Synax ownership (key: {typeof(TKey).Name}) ";

        public override int GetServiceProviderHashCode() => typeof(TKey).GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo[$"Synax:Ownership:{typeof(TKey).Name}"] = "1";
    }
}

internal sealed class OwnershipConventionSetPlugin<TKey> : IConventionSetPlugin
    where TKey : notnull
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new OwnershipFilterValidationConvention<TKey>());
        return conventionSet;
    }
}

/// <summary>
/// Fails model finalization when an ownership-scoped entity has no ownership query filter.
/// The interceptor alone only stamps owners on insert; without the filter every query returns
/// rows across all owners.
/// </summary>
internal sealed class OwnershipFilterValidationConvention<TKey> : IModelFinalizingConvention
    where TKey : notnull
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // Filters sit on hierarchy roots; ApplyOwnershipQueryFilters fail-fasts on scoped
            // descendants of unscoped roots, so validating the roots covers everything.
            if (entityType.IsOwned() || entityType.BaseType is not null)
            {
                continue;
            }

            var clrType = entityType.ClrType;
            var isOwnershipScoped = typeof(IHasOwner<TKey>).IsAssignableFrom(clrType)
                || OwnershipModelBuilderExtensions.IsOptionalOwnerOf<TKey>(clrType);

            if (!isOwnershipScoped)
            {
                continue;
            }

            if (entityType.FindDeclaredQueryFilter(OwnershipModelBuilderExtensions.FilterName) is null)
            {
                throw new InvalidOperationException(
                    $"Entity type '{entityType.DisplayName()}' is ownership-scoped and the ownership " +
                    $"interceptor is configured, but no ownership query filter is applied — every query " +
                    $"would return rows belonging to other owners (a tenant-data leak). Call " +
                    $"modelBuilder.ApplyOwnershipQueryFilters<{typeof(TKey).Name}>(() => ownerProvider.GetCurrentOwnerId()) " +
                    $"at the end of OnModelCreating, where ownerProvider is a member of the DbContext. " +
                    $"To intentionally use owner stamping without query filters, add the interceptor via " +
                    $"optionsBuilder.AddInterceptors(new OwnershipInterceptor<{typeof(TKey).Name}>(...)) instead.");
            }
        }
    }
}
