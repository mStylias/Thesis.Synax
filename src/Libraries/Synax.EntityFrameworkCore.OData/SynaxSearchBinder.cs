using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.OData;
using Microsoft.OData.UriParser;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Binds <c>$search</c> to a predicate over the members an app declared with
/// <see cref="SynaxODataOptions.Searchable{T}"/>: each search term becomes a case-insensitive
/// "contains" across those members OR'd together, combined by the search expression's own
/// <c>AND</c>/<c>OR</c>/<c>NOT</c> structure (space-separated terms are an implicit <c>AND</c>,
/// a quoted phrase is one term).
/// </summary>
/// <remarks>
/// The emitted shape is <c>member != null &amp;&amp; member.ToLower().Contains(term)</c>, which is the
/// one form that behaves identically in SQL (<c>lower(col) LIKE '%term%'</c> on Npgsql) and in
/// LINQ-to-Objects — so the <c>Synax.EntityFrameworkCore.Testing</c> fakes stay honest.
/// <c>EF.Functions.ILike</c> would be the Postgres-native alternative but is provider-specific and
/// throws in memory; this binder is the single seam where a provider-aware strategy would slot in.
/// The pattern cannot use a btree index — add <c>pg_trgm</c> or a <c>tsvector</c> column if a real
/// workload needs one.
/// </remarks>
internal sealed class SynaxSearchBinder(SynaxODataOptions options) : ISearchBinder
{
    private static readonly MethodInfo ToLowerMethod =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo ContainsMethod =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    public Expression BindSearch(SearchClause searchClause, QueryBinderContext context)
    {
        var elementType = context.ElementClrType;
        var members = options.SearchableMembersFor(elementType);

        // Reached for a type an $expand nested a $search into as well, which is where this is the
        // first place the nested element type is known.
        if (members.Count == 0)
            throw new ODataException(SearchGuardrails.NotAvailable(elementType.Name));

        var parameter = context.CurrentParameter.Type == elementType
            ? context.CurrentParameter
            : Expression.Parameter(elementType, "$it");

        return Expression.Lambda(Bind(searchClause.Expression, parameter, members), parameter);
    }

    private static Expression Bind(
        SingleValueNode node,
        ParameterExpression parameter,
        IReadOnlyList<LambdaExpression> members)
    {
        switch (node)
        {
            case SearchTermNode term:
                return BindTerm(term.Text, parameter, members);

            case BinaryOperatorNode { OperatorKind: BinaryOperatorKind.And } binary:
                return Expression.AndAlso(
                    Bind(binary.Left, parameter, members),
                    Bind(binary.Right, parameter, members));

            case BinaryOperatorNode { OperatorKind: BinaryOperatorKind.Or } binary:
                return Expression.OrElse(
                    Bind(binary.Left, parameter, members),
                    Bind(binary.Right, parameter, members));

            case UnaryOperatorNode { OperatorKind: UnaryOperatorKind.Not } unary:
                return Expression.Not(Bind(unary.Operand, parameter, members));

            case ConvertNode convert:
                return Bind(convert.Source, parameter, members);

            default:
                throw new ODataException(
                    $"The $search expression uses an unsupported construct ('{node.Kind}'). " +
                    "Supported: terms, quoted phrases, AND, OR and NOT.");
        }
    }

    /// <summary>One term matches when any declared member case-insensitively contains it.</summary>
    private static Expression BindTerm(
        string term,
        ParameterExpression parameter,
        IReadOnlyList<LambdaExpression> members)
    {
        var needle = Expression.Constant(term.ToLowerInvariant());

        Expression? predicate = null;
        foreach (var member in members)
        {
            var access = ParameterRebinder.Rebind(member, parameter);
            var contains = Expression.Call(Expression.Call(access, ToLowerMethod), ContainsMethod, needle);
            Expression matches = NotNullGuard(access) is { } guard
                ? Expression.AndAlso(guard, contains)
                : contains;

            predicate = predicate is null ? matches : Expression.OrElse(predicate, matches);
        }

        // The caller rejects an empty member set, so at least one term was produced.
        return predicate!;
    }

    /// <summary>
    /// <c>x.A != null &amp;&amp; x.A.B != null</c> — every reference-typed step of the access chain, so a
    /// null intermediate is a non-match in memory instead of a <see cref="NullReferenceException"/>.
    /// SQL handles this on its own; the extra terms are redundant there but harmless.
    /// </summary>
    private static Expression? NotNullGuard(Expression access)
    {
        var steps = new List<Expression>();

        for (var current = access; current is MemberExpression member; current = member.Expression!)
        {
            if (!current.Type.IsValueType)
                steps.Add(Expression.NotEqual(current, Expression.Constant(null, current.Type)));

            if (member.Expression is null)
                break;
        }

        if (steps.Count == 0)
            return null;

        // Outermost access is collected first; guard the shallowest step first so short-circuiting works.
        steps.Reverse();
        return steps.Aggregate(Expression.AndAlso);
    }

    /// <summary>Grafts a declared member lambda onto the binder's own parameter.</summary>
    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        public static Expression Rebind(LambdaExpression member, ParameterExpression parameter)
            => new ParameterRebinder(member.Parameters[0], parameter).Visit(member.Body)!;

        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
