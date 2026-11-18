using System.Linq.Expressions;
using Synax.Mapper.Internal;

namespace Synax.Mapper.Core;

/// <summary>
/// Holds both the expression tree (for IQueryable projection) and the compiled
/// delegate (for in-memory mapping) of a single source-to-destination mapping.
/// Instances are created once during configuration and reused for the lifetime of
/// the <see cref="MapperConfiguration"/>.
/// </summary>
public sealed class TypeMap
{
    /// <summary>
    /// The raw expression tree. Pass this to <c>IQueryable.Select()</c> for
    /// EF Core / LINQ-to-SQL translation.
    /// </summary>
    public LambdaExpression Expression { get; }

    /// <summary>
    /// The compiled delegate for fast in-memory mapping. Generated once via
    /// <see cref="LambdaExpression.Compile()"/> and cached.
    /// </summary>
    public Delegate CompiledDelegate { get; }

    /// <summary>
    /// Untyped delegate that accepts and returns <see cref="object"/>.
    /// Built at startup by inlining the mapping body with cast expressions,
    /// so the JIT compiles it to the same quality code as the typed delegate.
    /// </summary>
    internal Func<object, object> UntypedDelegate { get; }

    /// <summary>
    /// The update expression (<c>Action&lt;TSource, TDest&gt;</c>) that assigns the same
    /// bindings onto an existing destination instance instead of constructing a new one.
    /// Only writable members are assigned; nested objects and collections are replaced.
    /// In-memory only — not intended for IQueryable translation.
    /// </summary>
    public LambdaExpression UpdateExpression { get; }

    private readonly Lazy<Action<object, object>> _untypedUpdateDelegate;

    /// <summary>
    /// Untyped update delegate, compiled on first use so maps that are never used for
    /// updates pay no startup cost.
    /// </summary>
    internal Action<object, object> UntypedUpdateDelegate => _untypedUpdateDelegate.Value;

    public Type SourceType { get; }
    public Type DestinationType { get; }

    /// <summary>
    /// Destination members (public writable properties) that no binding produces a
    /// value for — neither explicitly mapped, auto-mapped, nor ignored. They are left
    /// at their default value when mapping. Empty when the destination is fully mapped.
    /// Inspected by <see cref="MapperConfiguration.AssertConfigurationIsValid"/>.
    /// </summary>
    public IReadOnlyList<string> UnmappedMembers { get; }

    /// <summary>
    /// The names of the <see cref="MapParameter{T}"/>s this mapping reads, its own and those of every
    /// map inlined into it. Empty for a mapping that needs no per-call value.
    /// </summary>
    public IReadOnlyCollection<string> ParameterNames => _parameters.Keys.ToArray();

    private readonly IReadOnlyDictionary<string, IMapParameter> _parameters;

    internal TypeMap(
        LambdaExpression expression,
        LambdaExpression updateExpression,
        Type sourceType,
        Type destinationType,
        IReadOnlyList<string> unmappedMembers,
        IReadOnlyDictionary<string, IMapParameter> parameters)
    {
        Expression = expression;
        _parameters = parameters;
        UpdateExpression = updateExpression;
        CompiledDelegate = expression.Compile();
        SourceType = sourceType;
        DestinationType = destinationType;
        UnmappedMembers = unmappedMembers;

        // Build: (object src) => (object){ original body with parameter replaced by (TSource)src }
        var objParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "src");
        var castToSource = System.Linq.Expressions.Expression.Convert(objParam, sourceType);
        var inlinedBody = ParameterReplacer.Replace(expression.Body, expression.Parameters[0], castToSource);
        var castToObject = System.Linq.Expressions.Expression.Convert(inlinedBody, typeof(object));

        UntypedDelegate = System.Linq.Expressions.Expression.Lambda<Func<object, object>>(
            castToObject, objParam).Compile();

        _untypedUpdateDelegate = new Lazy<Action<object, object>>(BuildUntypedUpdateDelegate);
    }

    /// <summary>
    /// Builds <c>(object src, object dest) => { update body with parameters replaced
    /// by (TSource)src / (TDest)dest }</c> — same inlining technique as
    /// <see cref="UntypedDelegate"/>.
    /// </summary>
    private Action<object, object> BuildUntypedUpdateDelegate()
    {
        var srcParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "src");
        var destParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "dest");

        var body = ParameterReplacer.Replace(
            UpdateExpression.Body,
            UpdateExpression.Parameters[0],
            System.Linq.Expressions.Expression.Convert(srcParam, SourceType));
        body = ParameterReplacer.Replace(
            body,
            UpdateExpression.Parameters[1],
            System.Linq.Expressions.Expression.Convert(destParam, DestinationType));

        return System.Linq.Expressions.Expression.Lambda<Action<object, object>>(
            body, srcParam, destParam).Compile();
    }

    /// <summary>
    /// Returns the projection expression with strong typing.
    /// </summary>
    public Expression<Func<TSource, TDest>> GetExpression<TSource, TDest>()
        => (Expression<Func<TSource, TDest>>)Expression;

    /// <summary>
    /// Returns the projection expression with each <see cref="MapParameter{T}"/> read replaced by its
    /// value in <paramref name="parameters"/>. Every parameter the mapping reads must be supplied, and
    /// only those; a mapping that reads none accepts <c>null</c> or an empty set and returns
    /// <see cref="Expression"/> itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">A parameter is missing or unknown.</exception>
    /// <exception cref="ArgumentException">A value has the wrong type, or is null for a non-nullable value type.</exception>
    public LambdaExpression GetExpression(IReadOnlyDictionary<string, object?>? parameters)
        => MapParameterBinder.Bind(Expression, _parameters, parameters, SourceType, DestinationType);

    /// <summary>
    /// Returns the compiled mapping delegate with strong typing.
    /// </summary>
    public Func<TSource, TDest> GetDelegate<TSource, TDest>()
        => (Func<TSource, TDest>)CompiledDelegate;
}
