using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Synax.Mapper.Core;

/// <summary>
/// Provides in-memory mapping between objects and access to projection expressions
/// for IQueryable providers such as EF Core.
/// </summary>
public interface IMapper
{
    /// <summary>
    /// Maps <paramref name="source"/> to a new <typeparamref name="TDest"/> instance;
    /// <c>null</c> maps to <c>null</c>.
    /// The source type is resolved at runtime from the argument; when the runtime type
    /// itself has no registered mapping (e.g. an EF Core lazy-loading proxy), the
    /// closest mapped base type is used.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    TDest? Map<TDest>(object? source);

    /// <summary>
    /// Maps <paramref name="source"/> onto the <b>existing</b>
    /// <paramref name="destination"/> instance and returns it — the update pattern for
    /// PUT endpoints and tracked EF entities. Applies the same bindings as the
    /// constructing overload to every writable destination member; nested objects and
    /// collections are replaced, not merged. Ignored and unmapped members are left
    /// untouched. A <c>null</c> source returns the destination unchanged.
    /// </summary>
    TDest Map<TDest>(object? source, TDest destination) where TDest : class;

    /// <summary>
    /// Maps each element in <paramref name="source"/> to <typeparamref name="TDest"/>;
    /// a <c>null</c> sequence maps to an empty list.
    /// The source type is resolved per element, so heterogeneous lists are supported.
    /// <c>null</c> elements map to <c>default</c> (<c>null</c> for reference types).
    /// </summary>
    List<TDest> MapList<TDest>(IEnumerable<object?>? source);

    /// <summary>
    /// Returns the raw <see cref="Expression{TDelegate}"/> for the mapping.
    /// Use this directly in <c>IQueryable.Select()</c> for EF Core projection.
    /// The returned expression is a <c>MemberInit</c> tree that EF Core can fully
    /// translate to SQL. Throws when the mapping reads a <see cref="MapParameter{T}"/> — use the
    /// overload taking <see cref="MapParameters"/>.
    /// </summary>
    Expression<Func<TSource, TDest>> GetExpression<TSource, TDest>();

    /// <summary>
    /// Returns the projection expression with every <see cref="MapParameter{T}"/> it reads bound to
    /// its value in <paramref name="parameters"/>. Each value reaches EF Core as a SQL parameter,
    /// so one cached query plan serves every value.
    /// </summary>
    Expression<Func<TSource, TDest>> GetExpression<TSource, TDest>(IReadOnlyDictionary<string, object?>? parameters);

    /// <summary>
    /// Returns the underlying <see cref="TypeMap"/> for advanced scenarios
    /// such as expression composition, custom validation, or debugging.
    /// </summary>
    TypeMap GetTypeMap<TSource, TDest>();

    /// <summary>
    /// Returns the underlying <see cref="TypeMap"/> for types only known at runtime,
    /// e.g. when projecting an <see cref="IQueryable"/> whose element type is inferred.
    /// </summary>
    TypeMap GetTypeMap(Type sourceType, Type destinationType);
}
