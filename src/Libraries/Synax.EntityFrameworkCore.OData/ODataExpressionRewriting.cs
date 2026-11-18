using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Applies the registered <see cref="IODataExpressionRewriter"/>s to an applied query, with the
/// endpoint's source query hidden behind a placeholder while they run.
/// </summary>
internal sealed class ODataExpressionRewriting
{
    private static readonly ODataExpressionRewriting None = new(null!, []);

    private readonly Expression _source;
    private readonly IODataExpressionRewriter[] _rewriters;

    private ODataExpressionRewriting(Expression source, IODataExpressionRewriter[] rewriters)
    {
        _source = source;
        _rewriters = rewriters;
    }

    public static ODataExpressionRewriting For(IQueryable source, HttpRequest request)
    {
        var rewriters = request.HttpContext.RequestServices?.GetService(typeof(IEnumerable<IODataExpressionRewriter>))
            as IEnumerable<IODataExpressionRewriter>;

        IODataExpressionRewriter[] registered = rewriters is null ? [] : [.. rewriters];

        return registered.Length == 0 ? None : new ODataExpressionRewriting(source.Expression, registered);
    }

    /// <exception cref="InvalidOperationException">
    /// The applied query does not contain the source query, so what the options added cannot be told
    /// apart from what the endpoint wrote.
    /// </exception>
    public IQueryable Apply(IQueryable applied)
    {
        if (_rewriters.Length == 0 || ReferenceEquals(applied.Expression, _source))
            return applied;

        var placeholder = Expression.Parameter(_source.Type, "source");
        var hidden = new NodeSwap(_source, placeholder);
        var expression = hidden.Visit(applied.Expression)!;

        if (!hidden.Found)
            throw new InvalidOperationException(
                "The OData query no longer contains the endpoint's source query, so the registered " +
                "IODataExpressionRewriters cannot be limited to what the query options added.");

        foreach (var rewriter in _rewriters)
            expression = rewriter.Rewrite(expression);

        return applied.Provider.CreateQuery(new NodeSwap(placeholder, _source).Visit(expression)!);
    }

    /// <summary>Replaces one node, by reference, wherever it occurs.</summary>
    private sealed class NodeSwap(Expression from, Expression to) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (!ReferenceEquals(node, from))
                return base.Visit(node);

            Found = true;
            return to;
        }
    }
}
