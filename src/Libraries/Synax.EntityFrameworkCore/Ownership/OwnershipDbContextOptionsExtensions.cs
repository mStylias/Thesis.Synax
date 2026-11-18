using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.EntityFrameworkCore.Ownership;

/// <summary>
/// Extension methods for adding the <see cref="OwnershipInterceptor{TKey}"/> to DbContext options.
/// </summary>
public static class OwnershipDbContextOptionsExtensions
{
    /// <summary>
    /// Adds the <see cref="OwnershipInterceptor{TKey}"/> to the DbContext options, resolving the
    /// <see cref="IOwnerProvider{TKey}"/> from the application's service provider on each save —
    /// the registration made by <c>WithOwnership</c> is sufficient, no constructed instance needed.
    /// <para>
    /// Also enables startup validation: model building fails if any ownership-scoped entity lacks
    /// the ownership query filter (see <c>ApplyOwnershipQueryFilters</c>), so a forgotten filter
    /// surfaces as an error instead of a silent cross-tenant data leak.
    /// </para>
    /// </summary>
    public static DbContextOptionsBuilder AddOwnershipInterceptor<TKey>(
        this DbContextOptionsBuilder optionsBuilder)
        where TKey : notnull
    {
        optionsBuilder.AddInterceptors(new OwnershipInterceptor<TKey>());
        return optionsBuilder.AddOwnershipExtension<TKey>();
    }

    /// <summary>
    /// Adds the <see cref="OwnershipInterceptor{TKey}"/> to the DbContext options with an explicit
    /// <paramref name="ownerProvider"/> instance. Also enables the query-filter startup validation
    /// (see <see cref="AddOwnershipInterceptor{TKey}(DbContextOptionsBuilder)"/>).
    /// </summary>
    public static DbContextOptionsBuilder AddOwnershipInterceptor<TKey>(
        this DbContextOptionsBuilder optionsBuilder,
        IOwnerProvider<TKey> ownerProvider)
        where TKey : notnull
    {
        optionsBuilder.AddInterceptors(new OwnershipInterceptor<TKey>(ownerProvider));
        return optionsBuilder.AddOwnershipExtension<TKey>();
    }

    private static DbContextOptionsBuilder AddOwnershipExtension<TKey>(this DbContextOptionsBuilder optionsBuilder)
        where TKey : notnull
    {
        var extension = optionsBuilder.Options.FindExtension<OwnershipOptionsExtension<TKey>>()
            ?? new OwnershipOptionsExtension<TKey>();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return optionsBuilder;
    }
}
