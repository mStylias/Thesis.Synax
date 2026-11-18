using System.Collections.Concurrent;
using System.Linq.Expressions;
using Synax.EntityFrameworkCore.Abstractions.Auditing;

namespace Synax.EntityFrameworkCore.Auditing;

/// <summary>
/// Shared audit-stamping logic used by the <see cref="AuditingInterceptor"/> for tracked saves,
/// by the bulk insert path (which bypasses the change tracker), and by the Testing package's
/// fakes — one implementation, identical semantics everywhere.
/// <para>
/// Semantics: <c>CreatedAt</c>/<c>CreatedById</c> are stamped on insert only when they still hold
/// their default value (explicit values, e.g. for imports or backdating, win). <c>UpdatedAt</c>/
/// <c>ModifiedById</c> are overwritten on every modification — that is their meaning.
/// </para>
/// </summary>
internal static class AuditStamper
{
    private sealed record AuditAccessors(
        Func<object, object?>? GetCreatedById,
        Action<object, object>? SetCreatedById,
        object? CreatedByIdDefault,
        Action<object, object>? SetModifiedById);

    private static readonly ConcurrentDictionary<Type, AuditAccessors> Accessors = new();

    internal static void StampCreation(object entity, DateTime utcNow, object? userId)
    {
        if (entity is IHasCreationTime hasCreationTime && hasCreationTime.CreatedAt == default)
        {
            hasCreationTime.CreatedAt = utcNow;
        }

        if (userId is null)
        {
            return;
        }

        var accessors = Accessors.GetOrAdd(entity.GetType(), Build);
        if (accessors.SetCreatedById is not null
            && Equals(accessors.GetCreatedById!(entity), accessors.CreatedByIdDefault))
        {
            accessors.SetCreatedById(entity, userId);
        }
    }

    internal static void StampModification(object entity, DateTime utcNow, object? userId)
    {
        if (entity is IHasModificationTime hasModificationTime)
        {
            hasModificationTime.UpdatedAt = utcNow;
        }

        if (userId is null)
        {
            return;
        }

        var accessors = Accessors.GetOrAdd(entity.GetType(), Build);
        accessors.SetModifiedById?.Invoke(entity, userId);
    }

    private static AuditAccessors Build(Type entityType)
    {
        var (getCreatedById, setCreatedById, createdByIdType) =
            BuildIdAccessors(entityType, typeof(ICreatedBy<,>), nameof(ICreatedBy<,>.CreatedById));
        var (_, setModifiedById, _) =
            BuildIdAccessors(entityType, typeof(IModifiedBy<,>), nameof(IModifiedBy<,>.ModifiedById));

        var createdByIdDefault = createdByIdType is { IsValueType: true }
            ? Activator.CreateInstance(createdByIdType)
            : null;

        return new AuditAccessors(getCreatedById, setCreatedById, createdByIdDefault, setModifiedById);
    }

    private static (Func<object, object?>? Get, Action<object, object>? Set, Type? PropertyType) BuildIdAccessors(
        Type entityType,
        Type interfaceDefinition,
        string propertyName)
    {
        var @interface = entityType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == interfaceDefinition);

        var property = @interface?.GetProperty(propertyName);
        if (property is null)
        {
            return (null, null, null);
        }

        var entityParam = Expression.Parameter(typeof(object), "entity");
        var typedProperty = Expression.Property(Expression.Convert(entityParam, @interface!), property);

        var getter = Expression.Lambda<Func<object, object?>>(
            Expression.Convert(typedProperty, typeof(object)), entityParam).Compile();

        var valueParam = Expression.Parameter(typeof(object), "userId");
        var setter = Expression.Lambda<Action<object, object>>(
            Expression.Assign(typedProperty, Expression.Convert(valueParam, property.PropertyType)),
            entityParam, valueParam).Compile();

        return (getter, setter, property.PropertyType);
    }
}
