using System.Linq.Expressions;
using System.Reflection;

namespace Synax.Crud.Internal;

/// <summary>
/// Per-entity key plumbing for the generic handlers, compiled once: the key property
/// (convention: a public property named <c>Id</c>), an EF-parameterizable equality predicate,
/// a getter, a v7-Guid generator for default keys, and an order-by-key combinator for paging.
/// </summary>
internal static class CrudEntityKey<TEntity>
    where TEntity : class
{
    internal static readonly PropertyInfo KeyProperty = ResolveKeyProperty();

    private static readonly Func<TEntity, object?> KeyGetter = BuildGetter();
    private static readonly Func<object, Expression<Func<TEntity, bool>>> PredicateFactory = BuildPredicateFactory();
    private static readonly Func<IQueryable<TEntity>, IQueryable<TEntity>> OrderByKeyFunc = BuildOrderByKey();
    private static readonly Action<TEntity>? GuidKeyGenerator = BuildGuidKeyGenerator();

    /// <summary><c>e => e.Id == id</c>. The id rides in a captured box member so EF caches one
    /// query plan and parameterizes the value (a bare Constant would compile a plan per id).</summary>
    internal static Expression<Func<TEntity, bool>> Predicate(object id) => PredicateFactory(id);

    internal static object? GetKey(TEntity entity) => KeyGetter(entity);

    /// <summary>Client-side key generation for creates, mirroring the bulk-insert path:
    /// a Guid key still at its default gets a v7 id; other key types are left to the store.</summary>
    internal static void GenerateKeyIfDefault(TEntity entity) => GuidKeyGenerator?.Invoke(entity);

    internal static IQueryable<TEntity> OrderByKey(IQueryable<TEntity> query) => OrderByKeyFunc(query);

    private static PropertyInfo ResolveKeyProperty()
        => typeof(TEntity).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
           ?? throw new InvalidOperationException(
               $"Entity '{typeof(TEntity).Name}' has no public 'Id' property — the generic CRUD " +
               "handlers locate the key by that convention. Entities with differently-named or " +
               "composite keys need handwritten handlers.");

    private sealed class KeyBox
    {
        public object Value = null!;
    }

    private static Func<object, Expression<Func<TEntity, bool>>> BuildPredicateFactory()
    {
        return id =>
        {
            var box = new KeyBox { Value = id };
            var entityParam = Expression.Parameter(typeof(TEntity), "e");

            // e => e.Id == (TProp)box.Value — box.Value is an evaluatable subtree, so EF
            // funcletizes it into a query parameter; LINQ-to-Objects (the fakes) just runs it.
            var body = Expression.Equal(
                Expression.Property(entityParam, KeyProperty),
                Expression.Convert(
                    Expression.Field(Expression.Constant(box), nameof(KeyBox.Value)),
                    KeyProperty.PropertyType));

            return Expression.Lambda<Func<TEntity, bool>>(body, entityParam);
        };
    }

    private static Func<TEntity, object?> BuildGetter()
    {
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        return Expression.Lambda<Func<TEntity, object?>>(
            Expression.Convert(Expression.Property(entityParam, KeyProperty), typeof(object)),
            entityParam).Compile();
    }

    private static Func<IQueryable<TEntity>, IQueryable<TEntity>> BuildOrderByKey()
    {
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var keyLambda = Expression.Lambda(Expression.Property(entityParam, KeyProperty), entityParam);

        var orderBy = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TEntity), KeyProperty.PropertyType);

        var queryParam = Expression.Parameter(typeof(IQueryable<TEntity>), "q");
        return Expression.Lambda<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(
            Expression.Call(orderBy, queryParam, Expression.Quote(keyLambda)),
            queryParam).Compile();
    }

    private static Action<TEntity>? BuildGuidKeyGenerator()
    {
        if (KeyProperty.PropertyType != typeof(Guid) || !KeyProperty.CanWrite)
            return null;

        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var valueParam = Expression.Parameter(typeof(Guid), "v");
        var setter = Expression.Lambda<Action<TEntity, Guid>>(
            Expression.Assign(Expression.Property(entityParam, KeyProperty), valueParam),
            entityParam, valueParam).Compile();

        return entity =>
        {
            if ((Guid)KeyGetter(entity)! == Guid.Empty)
                setter(entity, Guid.CreateVersion7());
        };
    }
}
