using System.Collections.Concurrent;
using System.Reflection;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Auditing;
using Synax.EntityFrameworkCore.Ownership;

namespace Synax.EntityFrameworkCore.Testing;

internal interface IFakeStore
{
    int SaveChanges();
}

/// <summary>
/// In-memory backing store for one entity type, mirroring EF change-tracker semantics:
/// staged adds/updates/removes become visible only after <see cref="SaveChanges"/>.
/// </summary>
internal sealed class FakeStore<TEntity> : IFakeStore
    where TEntity : class
{
    private readonly FakeUnitOfWorkOptions _options;
    private readonly IServiceProvider _ownerServices;
    private readonly List<TEntity> _pendingAdds = [];
    private readonly List<TEntity> _pendingUpdates = [];
    private readonly List<(TEntity Entity, bool Hard)> _pendingRemoves = [];

    internal FakeStore(FakeUnitOfWorkOptions options)
    {
        _options = options;
        _ownerServices = new FakeOwnerServiceProvider(options.CurrentOwnerId, options.CrossOwner);
    }

    internal List<TEntity> Committed { get; } = [];

    internal FakeUnitOfWorkOptions Options => _options;

    internal void Seed(IEnumerable<TEntity> entities) => Committed.AddRange(entities);

    internal void StageAdd(TEntity entity) => _pendingAdds.Add(entity);

    internal void StageUpdate(TEntity entity) => _pendingUpdates.Add(entity);

    internal void StageRemove(TEntity entity, bool hard) => _pendingRemoves.Add((entity, hard));

    /// <summary>Inserts directly into committed data, bypassing pending state (bulk-insert semantics).</summary>
    internal void InsertDirect(IReadOnlyList<TEntity> entities)
    {
        OwnershipAssigner.AssignOwners(entities, _ownerServices);

        var utcNow = _options.TimeProvider.GetUtcNow().UtcDateTime;
        var auditUserId = _options.CurrentUserId();
        foreach (var entity in entities)
        {
            AuditStamper.StampCreation(entity, utcNow, auditUserId);
        }

        Committed.AddRange(entities);
    }

    /// <summary>
    /// Returns the query snapshot. Unless <paramref name="ignoreFilters"/> is set, soft-deleted
    /// rows and rows outside the current owner's scope are excluded — mirroring the library's
    /// global query filters.
    /// </summary>
    internal IEnumerable<TEntity> Snapshot(bool ignoreFilters)
    {
        var source = Committed.ToList();

        return ignoreFilters || !_options.ApplyQueryFilters
            ? source
            : source.Where(e => FakeQueryFilters.IsVisible(e, _options));
    }

    public int SaveChanges()
    {
        var changes = 0;
        var utcNow = _options.TimeProvider.GetUtcNow().UtcDateTime;
        var auditUserId = _options.CurrentUserId();

        if (_pendingAdds.Count > 0)
        {
            OwnershipAssigner.AssignOwners(_pendingAdds, _ownerServices);

            foreach (var entity in _pendingAdds)
            {
                AuditStamper.StampCreation(entity, utcNow, auditUserId);
            }

            Committed.AddRange(_pendingAdds);
            changes += _pendingAdds.Count;
            _pendingAdds.Clear();
        }

        foreach (var entity in _pendingUpdates)
        {
            var target = FindCommitted(entity)
                ?? throw new InvalidOperationException(
                    $"Cannot update entity of type '{typeof(TEntity).Name}': no committed entity matches it (by reference or key).");

            // Same rule as the AuditingInterceptor: soft deletes are not modifications.
            if (entity is not ISoftDelete { IsDeleted: true })
            {
                AuditStamper.StampModification(entity, utcNow, auditUserId);
            }

            if (!ReferenceEquals(target, entity))
            {
                Committed[Committed.IndexOf(target)] = entity;
            }

            changes++;
        }

        _pendingUpdates.Clear();

        foreach (var (entity, hard) in _pendingRemoves)
        {
            var target = FindCommitted(entity)
                ?? throw new InvalidOperationException(
                    $"Cannot remove entity of type '{typeof(TEntity).Name}': no committed entity matches it (by reference or key).");

            if (!hard && target is ISoftDelete)
            {
                FakeSoftDelete.Apply(target, _options);
            }
            else
            {
                Committed.Remove(target);
            }

            changes++;
        }

        _pendingRemoves.Clear();

        return changes;
    }

    internal TEntity? FindCommitted(TEntity entity)
    {
        var byReference = Committed.FirstOrDefault(e => ReferenceEquals(e, entity));
        if (byReference is not null)
        {
            return byReference;
        }

        var keySelector = _options.GetKeySelector(typeof(TEntity));
        if (keySelector is null)
        {
            return null;
        }

        var key = keySelector(entity);
        return key is null ? null : FindByKey(key);
    }

    internal TEntity? FindByKey(object key)
    {
        var keySelector = _options.GetKeySelector(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Cannot resolve a key for '{typeof(TEntity).Name}': no property named 'Id' exists and no key selector " +
                $"was configured. Configure one via FakeUnitOfWorkOptions.UseKey<{typeof(TEntity).Name}>(...).");

        return Committed.FirstOrDefault(e => Equals(keySelector(e), key));
    }
}

/// <summary>
/// Emulates the library's global query filters (soft delete + ownership) over in-memory entities.
/// </summary>
internal static class FakeQueryFilters
{
    private sealed record OwnershipShape(PropertyInfo? RequiredOwnerId, PropertyInfo? OptionalOwnerId);

    private static readonly ConcurrentDictionary<Type, OwnershipShape> ShapeCache = new();

    internal static bool IsVisible(object entity, FakeUnitOfWorkOptions options)
    {
        if (entity is ISoftDelete { IsDeleted: true })
        {
            return false;
        }

        var shape = ShapeCache.GetOrAdd(entity.GetType(), Classify);

        // Owner scoping lifted: every owner's rows are visible, mirroring the cross-owner term
        // the real query filter carries.
        if (options.CrossOwner())
        {
            return true;
        }

        if (shape.RequiredOwnerId is not null)
        {
            var currentOwner = options.CurrentOwnerId();
            return currentOwner is not null && Equals(shape.RequiredOwnerId.GetValue(entity), currentOwner);
        }

        if (shape.OptionalOwnerId is not null)
        {
            var ownerId = shape.OptionalOwnerId.GetValue(entity);
            return ownerId is null || Equals(ownerId, options.CurrentOwnerId());
        }

        return true;
    }

    private static OwnershipShape Classify(Type type)
    {
        var requiredInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasOwner<>));

        if (requiredInterface is not null)
        {
            return new OwnershipShape(requiredInterface.GetProperty(nameof(IHasOwner<int>.OwnerId)), null);
        }

        var optionalInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasOptionalOwner<>));

        return new OwnershipShape(null, optionalInterface?.GetProperty(nameof(IHasOptionalOwner<object>.OwnerId)));
    }
}

/// <summary>
/// Applies the same soft-delete stamping as <c>SoftDeleteInterceptor</c> to in-memory entities.
/// </summary>
internal static class FakeSoftDelete
{
    internal static void Apply(object entity, FakeUnitOfWorkOptions options)
    {
        ((ISoftDelete)entity).IsDeleted = true;

        if (entity is IHasDeletionTime hasDeletionTime)
        {
            hasDeletionTime.DeletedAt = options.TimeProvider.GetUtcNow().UtcDateTime;
        }

        var userId = options.CurrentUserId();
        if (userId is null)
        {
            return;
        }

        var deletedByInterface = entity.GetType()
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDeletedBy<,>));

        deletedByInterface
            ?.GetProperty(nameof(IDeletedBy<,>.DeletedById))
            ?.SetValue(entity, userId);
    }
}

/// <summary>
/// Minimal <see cref="IServiceProvider"/> that serves <see cref="IOwnerProvider{TKey}"/> requests
/// from a single ambient owner-id accessor, so the library's <c>OwnershipAssigner</c> can be reused verbatim.
/// </summary>
internal sealed class FakeOwnerServiceProvider(Func<object?> currentOwnerId, Func<bool> crossOwner)
    : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IOwnerProvider<>))
        {
            var keyType = serviceType.GetGenericArguments()[0];
            return Activator.CreateInstance(
                typeof(FakeOwnerProvider<>).MakeGenericType(keyType), currentOwnerId, crossOwner);
        }

        return null;
    }
}

/// <summary>
/// An <see cref="IOwnerProvider{TKey}"/> backed by delegates — usable both internally and in
/// user test setups.
/// </summary>
public sealed class FakeOwnerProvider<TKey>(Func<object?> currentOwnerId, Func<bool> crossOwner)
    : IOwnerProvider<TKey>
    where TKey : notnull
{
    public FakeOwnerProvider(Func<object?> currentOwnerId) : this(currentOwnerId, () => false)
    {
    }

    public object? GetCurrentOwnerId() => crossOwner() ? null : currentOwnerId();

    public bool IsCrossOwner() => crossOwner();
}
