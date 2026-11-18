using System.Collections.Concurrent;
using System.Linq.Expressions;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.EntityFrameworkCore.Ownership;

/// <summary>
/// Shared owner-assignment logic used by the <see cref="OwnershipInterceptor{TKey}"/> for tracked
/// inserts and by the repository's bulk insert path (which bypasses the change tracker and would
/// otherwise skip owner stamping entirely).
/// </summary>
internal static class OwnershipAssigner
{
    /// <summary>
    /// Assigns the ambient owner to <paramref name="entity"/> implementing <c>IHasOwner&lt;TKey&gt;</c>
    /// when <c>OwnerId</c> has not been set explicitly. Throws when no owner is available.
    /// <para>
    /// <c>default(TKey)</c> is reserved as the "not set" sentinel (see <see cref="IHasOwner{TKey}.OwnerId"/>),
    /// so an explicit owner id equal to <c>default(TKey)</c> cannot be distinguished from an unset one.
    /// </para>
    /// </summary>
    internal static void AssignRequired<TKey>(object entity, object? rawOwnerId, bool crossOwner = false)
        where TKey : notnull
    {
        var requiredEntity = (IHasOwner<TKey>)entity;

        if (!EqualityComparer<TKey>.Default.Equals(requiredEntity.OwnerId, default!))
        {
            return;
        }

        // Cross-owner scoping widens reads only: with every owner in view there is no single
        // ambient owner to stamp, so guessing one would silently mis-own the row.
        if (crossOwner)
        {
            throw new InvalidOperationException(
                $"Cannot persist entity of type '{entity.GetType().Name}' implementing " +
                $"IHasOwner<{typeof(TKey).Name}>: owner scoping is lifted for this scope " +
                $"(BecomeAllOwners), so there is no ambient owner to assign. Set OwnerId explicitly " +
                $"before saving, or perform the write inside a BecomeOwner scope for the owner it belongs to.");
        }

        if (rawOwnerId is not TKey ownerId)
        {
            throw new InvalidOperationException(
                $"Cannot persist entity of type '{entity.GetType().Name}' implementing " +
                $"IHasOwner<{typeof(TKey).Name}>: no current owner is available and OwnerId was not set " +
                $"explicitly. Register an IOwnerProvider<{typeof(TKey).Name}> (e.g. via WithOwnership) " +
                $"that returns a non-null owner, or set OwnerId before saving.");
        }

        requiredEntity.OwnerId = ownerId;
    }

    /// <summary>
    /// Assigns the ambient owner to <paramref name="entity"/> implementing <c>IHasOptionalOwner&lt;TKey?&gt;</c>
    /// when <c>OwnerId</c> has not been set explicitly and an owner is available.
    /// </summary>
    // Compiled OwnerId accessors per entity type — the save path never re-walks
    // GetInterfaces()/GetProperty per added entity.
    private static readonly ConcurrentDictionary<Type, (Func<object, object?> Get, Action<object, object?> Set)>
        OptionalOwnerAccessors = new();

    internal static void AssignOptional(object entity, object? rawOwnerId)
    {
        var (get, set) = OptionalOwnerAccessors.GetOrAdd(entity.GetType(), BuildOptionalOwnerAccessors);

        if (get(entity) is not null || rawOwnerId is null)
        {
            return;
        }

        set(entity, rawOwnerId);
    }

    private static (Func<object, object?> Get, Action<object, object?> Set) BuildOptionalOwnerAccessors(Type entityType)
    {
        // Access OwnerId via the IHasOptionalOwner<> interface because the generic argument is
        // a nullable type (e.g. int?) which we can't express statically.
        var @interface = entityType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasOptionalOwner<>));

        var prop = @interface.GetProperty(nameof(IHasOptionalOwner<>.OwnerId))!;

        var entityParam = Expression.Parameter(typeof(object), "entity");
        var typedProperty = Expression.Property(Expression.Convert(entityParam, @interface), prop);
        var getter = Expression.Lambda<Func<object, object?>>(
            Expression.Convert(typedProperty, typeof(object)), entityParam).Compile();

        var valueParam = Expression.Parameter(typeof(object), "ownerId");
        var setter = Expression.Lambda<Action<object, object?>>(
            Expression.Assign(typedProperty, Expression.Convert(valueParam, prop.PropertyType)),
            entityParam, valueParam).Compile();

        return (getter, setter);
    }

    /// <summary>
    /// Applies the same owner assignment the <see cref="OwnershipInterceptor{TKey}"/> performs on
    /// tracked adds to entities about to be bulk-inserted. The owner id is resolved from the
    /// <see cref="IOwnerProvider{TKey}"/> registered in DI; when the entity type is not
    /// ownership-scoped this is a no-op.
    /// </summary>
    internal static void AssignOwners<TEntity>(IReadOnlyList<TEntity> entities, IServiceProvider serviceProvider)
        where TEntity : class
    {
        var requiredInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasOwner<>));

        if (requiredInterface is not null)
        {
            var keyType = requiredInterface.GetGenericArguments()[0];
            var (rawOwnerId, crossOwner) = ResolveOwner(serviceProvider, keyType);

            var assign = (Action<object, object?, bool>)typeof(OwnershipAssigner)
                .GetMethod(nameof(AssignRequired), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericMethod(keyType)
                .CreateDelegate(typeof(Action<object, object?, bool>));

            foreach (var entity in entities)
            {
                assign(entity, rawOwnerId, crossOwner);
            }

            return;
        }

        var optionalInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasOptionalOwner<>));

        if (optionalInterface is not null)
        {
            var keyArg = optionalInterface.GetGenericArguments()[0];
            var keyType = Nullable.GetUnderlyingType(keyArg) ?? keyArg;
            var (rawOwnerId, _) = ResolveOwner(serviceProvider, keyType);

            foreach (var entity in entities)
            {
                // Optional owners stay null under cross-owner scoping — a shared row, which is
                // the honest reading of "no single owner is in scope".
                AssignOptional(entity, rawOwnerId);
            }
        }
    }

    private static (object? OwnerId, bool CrossOwner) ResolveOwner(IServiceProvider serviceProvider, Type keyType)
    {
        var providerType = typeof(IOwnerProvider<>).MakeGenericType(keyType);
        var provider = serviceProvider.GetService(providerType);

        if (provider is null)
        {
            return (null, false);
        }

        var ownerId = providerType.GetMethod(nameof(IOwnerProvider<>.GetCurrentOwnerId))!.Invoke(provider, null);
        var crossOwner = (bool)providerType.GetMethod(nameof(IOwnerProvider<>.IsCrossOwner))!.Invoke(provider, null)!;

        return (ownerId, crossOwner);
    }
}
