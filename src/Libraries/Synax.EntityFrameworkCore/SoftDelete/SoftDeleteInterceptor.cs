using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.SoftDelete;

/// <summary>
/// A <see cref="SaveChangesInterceptor"/> that converts delete operations on entities
/// implementing <see cref="ISoftDelete"/> into soft deletes. Only the soft-delete properties
/// (<see cref="ISoftDelete.IsDeleted"/>, <see cref="IHasDeletionTime.DeletedAt"/>,
/// <c>DeletedById</c>) are marked as modified, so other columns are never overwritten
/// with potentially stale in-memory values.
/// <para>
/// Entities explicitly marked for hard deletion via <see cref="HardDeleteTracker"/> are left unchanged.
/// </para>
/// <para>
/// The <see cref="ISoftDeleteUserProvider"/> used for audit can either be passed to the constructor
/// or, when omitted, is resolved from the application's service provider (as configured via
/// <c>AddDbContext</c>) on each save, so a DI registration alone is sufficient.
/// </para>
/// </summary>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly ISoftDeleteUserProvider? _userProvider;
    private readonly TimeProvider? _timeProvider;

    public SoftDeleteInterceptor(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider;
    }

    public SoftDeleteInterceptor(ISoftDeleteUserProvider userProvider, TimeProvider? timeProvider = null)
    {
        _userProvider = userProvider;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ConvertToSoftDelete(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ConvertToSoftDelete(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ClearHardDeleteMarks(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ClearHardDeleteMarks(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Marks are cleared only after a successful save. Clearing them during <c>SavingChanges</c>
    /// would drop them before the save is durable, so a retried save (e.g. via a retrying
    /// execution strategy) would wrongly convert the pending hard deletes into soft deletes.
    /// </summary>
    private static void ClearHardDeleteMarks(DbContext? context)
    {
        if (context is not null)
        {
            HardDeleteTracker.ClearMarks(context);
        }
    }

    private void ConvertToSoftDelete(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var deletedEntries = context.ChangeTracker
            .Entries()
            .Where(e => e.State == EntityState.Deleted && e.Entity is ISoftDelete)
            .ToList();

        if (deletedEntries.Count == 0)
        {
            return;
        }

        var userProvider = _userProvider ?? ApplicationServices.GetService<ISoftDeleteUserProvider>(context);
        var timeProvider = _timeProvider ?? ApplicationServices.GetService<TimeProvider>(context) ?? TimeProvider.System;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in deletedEntries)
        {
            if (HardDeleteTracker.IsMarkedForHardDelete(context, entry.Entity))
            {
                continue;
            }

            entry.State = EntityState.Modified;

            var softDelete = (ISoftDelete)entry.Entity;
            softDelete.IsDeleted = true;

            if (entry.Entity is IHasDeletionTime hasDeletionTime)
            {
                hasDeletionTime.DeletedAt = utcNow;
            }

            var deletedBySet = TrySetDeletedBy(entry.Entity, userProvider);

            // Persist only the soft-delete columns; any other property still carries the
            // in-memory value, which could overwrite changes made elsewhere with stale data.
            foreach (var property in entry.Properties)
            {
                property.IsModified = property.Metadata.Name switch
                {
                    nameof(ISoftDelete.IsDeleted) => true,
                    nameof(IHasDeletionTime.DeletedAt) when entry.Entity is IHasDeletionTime => true,
                    nameof(IDeletedBy<,>.DeletedById) when deletedBySet => true,
                    _ => false
                };
            }
        }
    }

    // Compiled DeletedById setters, cached per entity type (null = type has no IDeletedBy<,>),
    // so the save path never re-walks GetInterfaces()/GetProperty per deleted entity.
    private static readonly ConcurrentDictionary<Type, Action<object, object>?> DeletedBySetters = new();

    private static bool TrySetDeletedBy(object entity, ISoftDeleteUserProvider? userProvider)
    {
        var userId = userProvider?.GetCurrentUserId();
        if (userId is null)
        {
            return false;
        }

        var setter = DeletedBySetters.GetOrAdd(entity.GetType(), BuildDeletedBySetter);
        if (setter is null)
        {
            return false;
        }

        setter(entity, userId);
        return true;
    }

    private static Action<object, object>? BuildDeletedBySetter(Type entityType)
    {
        var deletedByInterface = entityType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDeletedBy<,>));

        var property = deletedByInterface?.GetProperty(nameof(IDeletedBy<,>.DeletedById));
        if (property is null)
        {
            return null;
        }

        var entityParam = Expression.Parameter(typeof(object), "entity");
        var valueParam = Expression.Parameter(typeof(object), "userId");
        var assign = Expression.Assign(
            Expression.Property(Expression.Convert(entityParam, deletedByInterface!), property),
            Expression.Convert(valueParam, property.PropertyType));

        return Expression.Lambda<Action<object, object>>(assign, entityParam, valueParam).Compile();
    }
}
