using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Synax.EntityFrameworkCore.Abstractions.Auditing;

namespace Synax.EntityFrameworkCore.Patching;

/// <summary>
/// Per-entity-type modification-audit setters for the patch execute path, built once —
/// the exact counterpart of the soft-delete executor's <c>DeletedAt</c>/<c>DeletedById</c>
/// cache, but for <see cref="IHasModificationTime.UpdatedAt"/> and
/// <c>IModifiedBy&lt;,&gt;.ModifiedById</c>.
/// </summary>
internal static class PatchAuditSetters<TEntity>
    where TEntity : class
{
    /// <summary><c>e => e.UpdatedAt</c>, or null when the entity has no modification time.</summary>
    internal static readonly Expression<Func<TEntity, DateTime?>>? UpdatedAtLambda = BuildUpdatedAtLambda();

    /// <summary>Invokes <c>builder.SetProperty(e => e.ModifiedById, (TUserKey)userId)</c>, or null.</summary>
    internal static readonly Action<UpdateSettersBuilder<TEntity>, object>? SetModifiedById = BuildModifiedByIdSetter();

    private static Expression<Func<TEntity, DateTime?>>? BuildUpdatedAtLambda()
    {
        if (!typeof(IHasModificationTime).IsAssignableFrom(typeof(TEntity)))
        {
            return null;
        }

        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var propertyAccess = Expression.Property(entityParam, nameof(IHasModificationTime.UpdatedAt));
        return Expression.Lambda<Func<TEntity, DateTime?>>(propertyAccess, entityParam);
    }

    private static Action<UpdateSettersBuilder<TEntity>, object>? BuildModifiedByIdSetter()
    {
        var modifiedByInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IModifiedBy<,>));

        if (modifiedByInterface is null)
        {
            return null;
        }

        var property = typeof(TEntity).GetProperty(nameof(IModifiedBy<,>.ModifiedById));
        if (property is null)
        {
            return null;
        }

        // e => e.ModifiedById
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var propertyLambda = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(typeof(TEntity), property.PropertyType),
            Expression.Property(entityParam, property),
            entityParam);

        var setPropertyMethod = typeof(UpdateSettersBuilder<TEntity>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(UpdateSettersBuilder<>.SetProperty))
            .Where(m => m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
            .First(m => m.GetParameters()[1].ParameterType.IsGenericParameter)
            .MakeGenericMethod(property.PropertyType);

        // (builder, userId) => builder.SetProperty<TUserKey>(propertyLambda, (TUserKey)userId)
        var builderParam = Expression.Parameter(typeof(UpdateSettersBuilder<TEntity>), "builder");
        var valueParam = Expression.Parameter(typeof(object), "userId");
        var call = Expression.Call(
            builderParam,
            setPropertyMethod,
            Expression.Constant(propertyLambda),
            Expression.Convert(valueParam, property.PropertyType));

        return Expression.Lambda<Action<UpdateSettersBuilder<TEntity>, object>>(call, builderParam, valueParam)
            .Compile();
    }
}
