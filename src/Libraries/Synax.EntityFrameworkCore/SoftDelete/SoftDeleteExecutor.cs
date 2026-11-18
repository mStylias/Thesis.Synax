using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.SoftDelete;

/// <summary>
/// Converts <c>ExecuteDeleteAsync</c> calls into <c>ExecuteUpdateAsync</c> calls
/// that set soft delete properties instead of permanently removing rows.
/// <para>
/// This is necessary because <c>ExecuteDelete</c> bypasses the change tracker entirely,
/// so the <see cref="SoftDeleteInterceptor"/> cannot intercept it.
/// </para>
/// </summary>
internal static class SoftDeleteExecutor
{
    internal static bool IsSoftDelete<TEntity>() =>
        typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity));

    internal static Task<int> ExecuteAsync<TEntity>(
        IQueryable<TEntity> query,
        object? userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
        where TEntity : class, ISoftDelete
    {
        return query.ExecuteUpdateAsync(s =>
        {
            s.SetProperty(e => e.IsDeleted, true);

            if (SetterCache<TEntity>.DeletedAtLambda is { } deletedAt)
            {
                s.SetProperty(deletedAt, utcNow);
            }

            if (userId is not null && SetterCache<TEntity>.SetDeletedById is { } setDeletedById)
            {
                setDeletedById(s, userId);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Per-entity-type setter metadata, built once. Reflection (interface walks, method lookup,
    /// <c>MakeGenericMethod</c>) happens on first use per <typeparamref name="TEntity"/>, not per call.
    /// </summary>
    private static class SetterCache<TEntity>
        where TEntity : class, ISoftDelete
    {
        /// <summary><c>e => e.DeletedAt</c>, or null when the entity has no deletion time.</summary>
        internal static readonly Expression<Func<TEntity, DateTime?>>? DeletedAtLambda = BuildDeletedAtLambda();

        /// <summary>Invokes <c>builder.SetProperty(e => e.DeletedById, (TUserKey)userId)</c>, or null.</summary>
        internal static readonly Action<UpdateSettersBuilder<TEntity>, object>? SetDeletedById = BuildDeletedByIdSetter();

        private static Expression<Func<TEntity, DateTime?>>? BuildDeletedAtLambda()
        {
            if (!typeof(IHasDeletionTime).IsAssignableFrom(typeof(TEntity)))
            {
                return null;
            }

            var entityParam = Expression.Parameter(typeof(TEntity), "e");
            var propertyAccess = Expression.Property(entityParam, nameof(IHasDeletionTime.DeletedAt));
            return Expression.Lambda<Func<TEntity, DateTime?>>(propertyAccess, entityParam);
        }

        private static Action<UpdateSettersBuilder<TEntity>, object>? BuildDeletedByIdSetter()
        {
            var deletedByInterface = typeof(TEntity).GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDeletedBy<,>));

            if (deletedByInterface is null)
            {
                return null;
            }

            var property = typeof(TEntity).GetProperty(nameof(IDeletedBy<,>.DeletedById));
            if (property is null)
            {
                return null;
            }

            // e => e.DeletedById
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
}
