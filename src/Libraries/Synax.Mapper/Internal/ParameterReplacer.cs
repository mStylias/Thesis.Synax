using System.Linq.Expressions;

namespace Synax.Mapper.Internal;

/// <summary>
/// Replaces all occurrences of a specific <see cref="ParameterExpression"/> within
/// an expression tree with an arbitrary replacement expression. This is the core
/// mechanism for inlining nested mapping expressions into a parent expression.
/// </summary>
internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParameter;
    private readonly Expression _replacement;

    private ParameterReplacer(ParameterExpression oldParameter, Expression replacement)
    {
        _oldParameter = oldParameter;
        _replacement = replacement;
    }

    protected override Expression VisitParameter(ParameterExpression node)
        => node == _oldParameter ? _replacement : base.VisitParameter(node);

    /// <summary>
    /// Replaces every occurrence of <paramref name="oldParameter"/> in <paramref name="body"/>
    /// with <paramref name="replacement"/>.
    /// </summary>
    public static Expression Replace(Expression body, ParameterExpression oldParameter, Expression replacement)
        => new ParameterReplacer(oldParameter, replacement).Visit(body);
}
