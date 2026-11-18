using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.EntityFrameworkCore.Ownership;

/// <summary>
/// Extension methods for applying ownership-scoped global query filters to the EF Core model.
/// </summary>
public static class OwnershipModelBuilderExtensions
{
    internal const string FilterName = "SynaxOwnership";

    /// <summary>
    /// Applies global query filters to all entity types that implement
    /// <see cref="IHasOwner{TKey}"/> or <see cref="IHasOptionalOwner{TKey}"/>,
    /// automatically scoping queries to the current owner provided by the given
    /// <see cref="IOwnerProvider{TKey}"/>.
    /// <para>
    /// <b>IHasOwner&lt;TKey&gt;</b>: rows are returned only when the provider supplies a non-null
    /// owner that matches <c>OwnerId</c>. When the provider returns <c>null</c>, no rows are returned.
    /// </para>
    /// <para>
    /// <b>IHasOptionalOwner&lt;TKey?&gt;</b>: rows with a <c>null</c> <c>OwnerId</c> (shared rows) are
    /// always returned; owned rows are returned only when the provider's value matches.
    /// </para>
    /// <para>
    /// Call this method at the end of your <c>OnModelCreating</c> override, after all entity
    /// configurations have been applied. Uses named query filters (<c>"SynaxOwnership"</c>) so it can
    /// coexist with <c>ApplySoftDeleteQueryFilters</c> and other filters.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="currentOwnerIdAccessor">
    /// A lambda that reads the current owner id. <b>Must</b> be passed from within
    /// <c>OnModelCreating</c> as <c>() =&gt; ownerProvider.GetCurrentOwnerId()</c> where
    /// <c>ownerProvider</c> is an <see cref="IOwnerProvider{TKey}"/> field or primary-constructor
    /// parameter of the DbContext. EF Core's parameter extractor recognises DbContext member
    /// references and re-evaluates them on every query execution — which is essential for
    /// ambient-state providers (AsyncLocal, IHttpContextAccessor). Lambdas that capture locals
    /// or extension-method parameters are constant-folded at first query compilation, baking a
    /// stale value into the cached SQL.
    /// </param>
    /// <param name="crossOwnerAccessor">
    /// Optional. When supplied — as <c>() =&gt; ownerProvider.IsCrossOwner()</c>, under the same
    /// DbContext-member rule as <paramref name="currentOwnerIdAccessor"/> — each filter gains a
    /// leading <c>crossOwner ||</c> term, so a scope that lifts owner scoping
    /// (<c>IUserContextScope.BecomeAllOwners</c>) sees every owner's rows. Omit it and the
    /// filters are exactly what they were: no extra term, no extra parameter in the SQL.
    /// </param>
    /// <typeparam name="TKey">The non-nullable owner identifier type (e.g. <c>int</c>, <c>Guid</c>).</typeparam>
    public static ModelBuilder ApplyOwnershipQueryFilters<TKey>(
        this ModelBuilder modelBuilder,
        Expression<Func<object?>> currentOwnerIdAccessor,
        Expression<Func<bool>>? crossOwnerAccessor = null)
        where TKey : notnull
    {
        var providerCallExpr = currentOwnerIdAccessor.Body; // object?
        var crossOwnerCallExpr = crossOwnerAccessor?.Body;  // bool

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Query filters cannot be applied to owned types.
            if (entityType.IsOwned())
            {
                continue;
            }

            // Query filters may only be applied to the root of an inheritance hierarchy.
            // If the root implements the ownership interface it receives the filter (covering
            // all derived types); otherwise the derived type would silently leak rows across
            // owners, so fail fast.
            if (entityType.BaseType is not null)
            {
                var rootType = entityType.GetRootType().ClrType;
                var isOwnershipScoped = typeof(IHasOwner<TKey>).IsAssignableFrom(clrType) || IsOptionalOwnerOf<TKey>(clrType);
                var rootIsOwnershipScoped = typeof(IHasOwner<TKey>).IsAssignableFrom(rootType) || IsOptionalOwnerOf<TKey>(rootType);

                if (isOwnershipScoped && !rootIsOwnershipScoped)
                {
                    throw new InvalidOperationException(
                        $"Entity type '{entityType.DisplayName()}' implements an ownership interface but is not " +
                        $"the root of its inheritance hierarchy. Query filters can only be applied to the root type " +
                        $"'{entityType.GetRootType().DisplayName()}', which must implement the ownership interface.");
                }

                continue;
            }

            if (typeof(IHasOwner<TKey>).IsAssignableFrom(clrType))
            {
                var filter = BuildRequiredFilter<TKey>(clrType, providerCallExpr, crossOwnerCallExpr);
                modelBuilder.Entity(clrType).HasQueryFilter(FilterName, filter);
            }
            else if (IsOptionalOwnerOf<TKey>(clrType))
            {
                var filter = BuildOptionalFilter(clrType, providerCallExpr, crossOwnerCallExpr);
                modelBuilder.Entity(clrType).HasQueryFilter(FilterName, filter);
            }
        }

        return modelBuilder;
    }

    /// <summary>
    /// Builds: (TEntity e) => (Nullable&lt;TKey&gt;)e.OwnerId == (Nullable&lt;TKey&gt;)providerCall,
    /// prefixed with <c>crossOwnerCall ||</c> when a cross-owner accessor was supplied.
    /// When the provider returns null, SQL evaluates OwnerId = NULL → false → no rows.
    /// </summary>
    private static LambdaExpression BuildRequiredFilter<TKey>(
        Type entityType,
        Expression providerCallExpr,
        Expression? crossOwnerCallExpr)
        where TKey : notnull
    {
        var parameter = Expression.Parameter(entityType, "e");
        var ownerIdProp = Expression.Property(parameter, nameof(IHasOwner<TKey>.OwnerId));

        // Normalize both sides to Nullable<TKey> for value types, or TKey for reference types.
        var nullableType = typeof(TKey).IsValueType
            ? typeof(Nullable<>).MakeGenericType(typeof(TKey))
            : typeof(TKey);

        var left = ownerIdProp.Type != nullableType
            ? Expression.Convert(ownerIdProp, nullableType)
            : (Expression)ownerIdProp;

        var right = Expression.Convert(providerCallExpr, nullableType);
        Expression body = Expression.Equal(left, right);

        return Expression.Lambda(WithCrossOwner(body, crossOwnerCallExpr), parameter);
    }

    /// <summary>
    /// Builds: (TEntity e) => e.OwnerId == null || e.OwnerId == (TOwnerIdType)providerCall,
    /// prefixed with <c>crossOwnerCall ||</c> when a cross-owner accessor was supplied.
    /// Shared rows (null OwnerId) are always visible; owned rows require a matching owner.
    /// </summary>
    private static LambdaExpression BuildOptionalFilter(
        Type entityType,
        Expression providerCallExpr,
        Expression? crossOwnerCallExpr)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var ownerIdProp = Expression.Property(parameter, nameof(IHasOptionalOwner<object>.OwnerId));
        var ownerIdType = ownerIdProp.Type; // e.g. int?, Guid?, string?

        // e.OwnerId == null
        var nullConst = Expression.Constant(null, ownerIdType);
        var isShared = Expression.Equal(ownerIdProp, nullConst);

        // e.OwnerId == (TOwnerIdType)providerCall
        var right = Expression.Convert(providerCallExpr, ownerIdType);
        var isOwned = Expression.Equal(ownerIdProp, right);

        var body = Expression.OrElse(isShared, isOwned);
        return Expression.Lambda(WithCrossOwner(body, crossOwnerCallExpr), parameter);
    }

    /// <summary>
    /// Prefixes a filter body with <c>crossOwnerCall ||</c>, or returns it untouched when no
    /// cross-owner accessor was supplied — apps that never lift owner scoping keep the exact
    /// SQL they had.
    /// </summary>
    private static Expression WithCrossOwner(Expression body, Expression? crossOwnerCallExpr)
        => crossOwnerCallExpr is null ? body : Expression.OrElse(crossOwnerCallExpr, body);

    /// <summary>
    /// Returns true if <paramref name="clrType"/> implements <c>IHasOptionalOwner&lt;T&gt;</c>
    /// where the underlying key type (unwrapping <see cref="Nullable{T}"/>) matches <typeparamref name="TKey"/>.
    /// </summary>
    internal static bool IsOptionalOwnerOf<TKey>(Type clrType)
    {
        var iface = clrType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasOptionalOwner<>));

        if (iface is null)
        {
            return false;
        }

        var keyArg = iface.GetGenericArguments()[0];
        var underlying = Nullable.GetUnderlyingType(keyArg) ?? keyArg;
        return underlying == typeof(TKey);
    }
}
