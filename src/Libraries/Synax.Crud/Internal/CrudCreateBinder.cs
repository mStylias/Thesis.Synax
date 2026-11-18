using System.Linq.Expressions;
using System.Reflection;

namespace Synax.Crud.Internal;

/// <summary>
/// Builds a new <typeparamref name="TEntity"/> from a create request by copying every public
/// readable request property onto the same-named entity property — the profile-less
/// name-matching convention (same rules and fail-fast diagnostics as <c>ExecutePatchAsync</c>).
/// Compiled once per (entity, request) pair; validated eagerly by the <c>AddSynaxCrud</c> scan.
/// </summary>
internal static class CrudCreateBinder<TEntity, TRequest>
    where TEntity : class, new()
    where TRequest : class
{
    private static readonly Lazy<Action<TRequest, TEntity>> Copy = new(Build);

    /// <summary>Forces the compile so misconfigurations surface at startup, not first request.</summary>
    internal static void Validate() => _ = Copy.Value;

    internal static TEntity Create(TRequest request)
    {
        var entity = new TEntity();
        Copy.Value(request, entity);
        CrudEntityKey<TEntity>.GenerateKeyIfDefault(entity);
        return entity;
    }

    private static Action<TRequest, TEntity> Build()
    {
        var requestParam = Expression.Parameter(typeof(TRequest), "request");
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var assignments = new List<Expression>();

        foreach (var requestProp in typeof(TRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!requestProp.CanRead)
                continue;

            var entityProp = typeof(TEntity).GetProperty(requestProp.Name, BindingFlags.Public | BindingFlags.Instance);
            if (entityProp is null || !entityProp.CanWrite)
            {
                throw new InvalidOperationException(
                    $"Create property '{typeof(TRequest).Name}.{requestProp.Name}' has no writable entity " +
                    $"property '{typeof(TEntity).Name}.{requestProp.Name}'. Create request members are " +
                    "matched to entity properties by name; remove the property or rename it.");
            }

            Expression value = Expression.Property(requestParam, requestProp);
            if (value.Type != entityProp.PropertyType)
            {
                try
                {
                    value = Expression.Convert(value, entityProp.PropertyType);
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException(
                        $"Create property '{typeof(TRequest).Name}.{requestProp.Name}' ({value.Type.Name}) cannot be " +
                        $"assigned to '{typeof(TEntity).Name}.{requestProp.Name}' ({entityProp.PropertyType.Name}): " +
                        "no conversion exists between the types.", ex);
                }
            }

            assignments.Add(Expression.Assign(Expression.Property(entityParam, entityProp), value));
        }

        return Expression.Lambda<Action<TRequest, TEntity>>(
            assignments.Count > 0 ? Expression.Block(assignments) : Expression.Empty(),
            requestParam, entityParam).Compile();
    }
}
