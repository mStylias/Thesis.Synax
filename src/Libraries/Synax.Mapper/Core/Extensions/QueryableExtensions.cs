using System.Linq.Expressions;

namespace Synax.Mapper.Core.Extensions;

/// <summary>
/// Extension methods for projecting <see cref="IQueryable{T}"/> using registered mappings.
/// These produce the same SQL as a hand-written <c>Select(s => new Dto { ... })</c>.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Projects each element of the queryable using the registered mapping from the
    /// queryable's element type to <typeparamref name="TDest"/>. The source type is
    /// inferred from the queryable, so only the destination needs to be specified.
    /// The expression tree is passed directly to the LINQ provider (EF Core),
    /// which translates it to an optimized SQL SELECT.
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var dtos = await dbContext.Orders
    ///     .Where(o => o.IsActive)
    ///     .ProjectTo&lt;OrderDto&gt;(mapper)
    ///     .ToListAsync();
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="source">The source queryable (typically a DbSet).</param>
    /// <param name="mapper">The mapper containing the registered mapping.</param>
    public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source, IMapper mapper)
        => source.ProjectTo<TDest>(mapper, parameters: null);

    /// <summary>
    /// Projects as <see cref="ProjectTo{TDest}(IQueryable, IMapper)"/>, binding every
    /// <see cref="MapParameter{T}"/> the mapping reads to its value in <paramref name="parameters"/>.
    /// Each value reaches the provider as a closure read, which EF Core sends as a SQL parameter, so
    /// one cached query plan serves every value.
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// query.ProjectTo&lt;ServiceDto&gt;(mapper, new MapParameters { ["languageId"] = languageId });
    /// </code>
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">A parameter the mapping reads is missing, or one it does not read was supplied.</exception>
    public static IQueryable<TDest> ProjectTo<TDest>(
        this IQueryable source, IMapper mapper, IReadOnlyDictionary<string, object?>? parameters)
    {
        var typeMap = mapper.GetTypeMap(source.ElementType, typeof(TDest));

        var select = Expression.Call(
            typeof(Queryable), nameof(Queryable.Select),
            [source.ElementType, typeof(TDest)],
            source.Expression, Expression.Quote(typeMap.GetExpression(parameters)));

        return source.Provider.CreateQuery<TDest>(select);
    }
}
