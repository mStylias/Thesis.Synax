using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

#pragma warning disable EF1001 // EntityQueryProvider is internal EF API — deriving from it is required
// so EF's marker operators (Include, IgnoreQueryFilters, ...) recognize the fake as an EF provider
// and embed themselves in the expression tree, where the stripping visitor can see them.

namespace Synax.EntityFrameworkCore.Testing;

internal interface IFakeAsyncQueryProvider : IAsyncQueryProvider
{
    object? ExecuteCore(Expression expression);
}

/// <summary>
/// An async-capable LINQ-to-Objects query provider. It makes EF Core's async operators
/// (<c>ToListAsync</c>, <c>FirstOrDefaultAsync</c>, ...) work against in-memory data, and strips
/// EF-only query-shaping calls (<c>Include</c>, <c>AsNoTracking</c>, <c>IgnoreQueryFilters</c>,
/// <c>AsSplitQuery</c>, <c>TagWith</c>, ...) before evaluation, so production query lambdas run
/// unmodified in unit tests. When <c>IgnoreQueryFilters</c> appears in the query, the unfiltered
/// snapshot is used — mirroring EF's global query filter behavior.
/// </summary>
internal sealed class FakeAsyncQueryProvider<TEntity> : EntityQueryProvider, IFakeAsyncQueryProvider
{
    private readonly Func<bool, IEnumerable<TEntity>> _snapshot;

    /// <param name="snapshot">
    /// Returns the current data snapshot; the argument is <c>true</c> when query filters
    /// should be ignored (i.e. <c>IgnoreQueryFilters()</c> appeared in the query).
    /// </param>
    internal FakeAsyncQueryProvider(Func<bool, IEnumerable<TEntity>> snapshot)
        : base(null!) // the query compiler is never used — every member is overridden
    {
        _snapshot = snapshot;
    }

    /// <summary>The root queryable this provider serves; compose queries on top of it.</summary>
    internal IQueryable<TEntity> CreateRoot() => new FakeAsyncEnumerable<TEntity>(this);

    public override IQueryable CreateQuery(Expression expression)
    {
        var elementType = expression.Type.GetGenericArguments().First();
        var queryableType = typeof(FakeAsyncEnumerable<>).MakeGenericType(elementType);
        return (IQueryable)Activator.CreateInstance(queryableType, this, expression)!;
    }

    public override IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new FakeAsyncEnumerable<TElement>(this, expression);

    public override object Execute(Expression expression) => ExecuteCore(expression)!;

    public override TResult Execute<TResult>(Expression expression) => (TResult)ExecuteCore(expression)!;

    public override TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        // EF calls this with TResult = Task<TActual>; evaluate synchronously and wrap.
        var actualResultType = typeof(TResult).GetGenericArguments().First();
        var result = ExecuteCore(expression);

        return (TResult)typeof(Task)
            .GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(actualResultType)
            .Invoke(null, [result])!;
    }

    public object? ExecuteCore(Expression expression)
    {
        var stripper = new EfMethodStrippingVisitor();
        var stripped = stripper.Visit(expression);

        var source = _snapshot(stripper.SawIgnoreQueryFilters).AsQueryable();
        var replaced = new RootReplacingVisitor(
            this,
            Expression.Constant(source, typeof(IQueryable<TEntity>))).Visit(stripped);

        // Sequence-shaped expressions (Where/Select chains) must be materialized as a query;
        // Execute would wrap them in a lambda whose IQueryable<> return type the LINQ-to-Objects
        // rewriter cannot satisfy. Scalar-shaped expressions (First, Count, ...) go through Execute.
        return typeof(IQueryable).IsAssignableFrom(replaced.Type)
            ? source.Provider.CreateQuery(replaced)
            : source.Provider.Execute(replaced);
    }
}

/// <summary>
/// An <see cref="IQueryable{T}"/> that also implements <see cref="IAsyncEnumerable{T}"/>,
/// backed by <see cref="FakeAsyncQueryProvider{TEntity}"/>.
/// </summary>
internal sealed class FakeAsyncEnumerable<TElement> : IOrderedQueryable<TElement>, IAsyncEnumerable<TElement>
{
    private readonly IFakeAsyncQueryProvider _provider;

    /// <summary>Creates a query root; its expression is a constant referencing itself.</summary>
    internal FakeAsyncEnumerable(IFakeAsyncQueryProvider provider)
    {
        _provider = provider;
        Expression = Expression.Constant(this);
    }

    public FakeAsyncEnumerable(IFakeAsyncQueryProvider provider, Expression expression)
    {
        _provider = provider;
        Expression = expression;
    }

    public Type ElementType => typeof(TElement);
    public Expression Expression { get; }
    public IQueryProvider Provider => _provider;

    public IEnumerator<TElement> GetEnumerator()
        => ((IEnumerable<TElement>)_provider.ExecuteCore(Expression)!).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public async IAsyncEnumerator<TElement> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        foreach (var item in this)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Removes EF Core-specific query-shaping method calls that LINQ to Objects cannot interpret.
/// They carry no in-memory semantics (object graphs are already "included", nothing is tracked),
/// with the exception of <c>IgnoreQueryFilters</c>, whose occurrence is recorded so the caller
/// can evaluate against unfiltered data.
/// </summary>
internal sealed class EfMethodStrippingVisitor : ExpressionVisitor
{
    private static readonly HashSet<string> StrippedMethods =
    [
        "Include",
        "ThenInclude",
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsTracking",
        "IgnoreQueryFilters",
        "IgnoreAutoIncludes",
        "AsSplitQuery",
        "AsSingleQuery",
        "TagWith",
        "TagWithCallSite"
    ];

    public bool SawIgnoreQueryFilters { get; private set; }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.IsStatic
            && node.Method.DeclaringType?.Namespace == "Microsoft.EntityFrameworkCore"
            && StrippedMethods.Contains(node.Method.Name)
            && node.Arguments.Count >= 1)
        {
            if (node.Method.Name == "IgnoreQueryFilters")
            {
                SawIgnoreQueryFilters = true;
            }

            return Visit(node.Arguments[0]);
        }

        return base.VisitMethodCall(node);
    }
}

/// <summary>
/// Replaces the fake query root (a constant referencing a <see cref="FakeAsyncEnumerable{TElement}"/>
/// served by the given provider) with a LINQ-to-Objects queryable over the data snapshot.
/// </summary>
internal sealed class RootReplacingVisitor : ExpressionVisitor
{
    private readonly IFakeAsyncQueryProvider _provider;
    private readonly Expression _replacement;

    internal RootReplacingVisitor(IFakeAsyncQueryProvider provider, Expression replacement)
    {
        _provider = provider;
        _replacement = replacement;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        return node.Value is IQueryable queryable && ReferenceEquals(queryable.Provider, _provider)
            ? _replacement
            : base.VisitConstant(node);
    }
}
