using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.SoftDelete;

public static class SoftDeleteModelBuilderExtensions
{
    internal const string FilterName = "SynaxSoftDelete";

    /// <summary>
    /// Applies a global query filter to all entity types that implement <see cref="ISoftDelete"/>,
    /// automatically excluding soft-deleted entities from all queries.
    /// <para>
    /// Call this method at the end of your <c>OnModelCreating</c> override, after all entity
    /// configurations have been applied, so that all <see cref="ISoftDelete"/> entities are discovered.
    /// Uses a named query filter (<c>"SynaxSoftDelete"</c>) so it can coexist with
    /// <c>ApplyOwnershipQueryFilters</c> and other named filters on the same entity.
    /// </para>
    /// </summary>
    public static ModelBuilder ApplySoftDeleteQueryFilters(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // Query filters cannot be applied to owned types.
            if (entityType.IsOwned())
            {
                continue;
            }

            // Query filters may only be applied to the root of an inheritance hierarchy.
            // If the root implements ISoftDelete it receives the filter (covering all derived
            // types); otherwise the derived type would silently go unfiltered, so fail fast.
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

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(FilterName, BuildFilter(entityType.ClrType));
        }

        return modelBuilder;
    }

    /// <summary>Builds: <c>(TEntity e) => !e.IsDeleted</c>.</summary>
    internal static LambdaExpression BuildFilter(Type entityClrType)
    {
        var parameter = Expression.Parameter(entityClrType, "e");
        var isDeletedProperty = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
        return Expression.Lambda(Expression.Not(isDeletedProperty), parameter);
    }
}
