using System.Linq.Expressions;
using System.Reflection;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// The string members of one exposed type that <c>$search</c> is allowed to look in, declared via
/// <see cref="SynaxODataOptions.Searchable{T}"/>. Stored as validated member-access lambdas so the
/// binder can graft them onto its own parameter — no OData types leak into application code.
/// </summary>
internal sealed class SearchableMemberSet
{
    private readonly List<LambdaExpression> _members = [];

    public IReadOnlyList<LambdaExpression> Members => _members;

    public void Add(LambdaExpression member)
    {
        Validate(member);

        // Store the bare member access (a Func<T, string?> declaration can carry a Convert node) so
        // the binder's null-guard walk sees a plain MemberExpression chain.
        var normalized = Expression.Lambda(Unwrap(member.Body), member.Parameters[0]);

        // Re-declaring the same path is a no-op rather than a duplicated OR term; modules that both
        // declare a shared type should not widen the predicate.
        if (_members.Any(existing => Describe(existing) == Describe(normalized)))
            return;

        _members.Add(normalized);
    }

    /// <summary>
    /// A searchable member must be a plain property/field access chain rooted at the lambda's
    /// parameter and ending in a <see cref="string"/> — anything else could not be translated to
    /// SQL, or would not be a member at all.
    /// </summary>
    private static void Validate(LambdaExpression member)
    {
        var body = Unwrap(member.Body);

        if (body.Type != typeof(string))
            throw new ArgumentException(
                $"Searchable member '{member}' must return a string; '{body.Type.Name}' is not searchable. " +
                "Expose a string member (e.g. project the value in your DTO) instead.",
                nameof(member));

        for (var current = body; ;)
        {
            switch (Unwrap(current))
            {
                case MemberExpression { Member: PropertyInfo or FieldInfo } access:
                    current = access.Expression ?? throw Invalid(member);
                    continue;
                case ParameterExpression parameter when parameter == member.Parameters[0]:
                    return;
                default:
                    throw Invalid(member);
            }
        }
    }

    private static ArgumentException Invalid(LambdaExpression member) => new(
        $"Searchable member '{member}' must be a property or field access on the searched type " +
        "(e.g. x => x.Name or x => x.Category.Name). Method calls, conditionals and computed " +
        "expressions cannot be translated to SQL.",
        nameof(member));

    private static Expression Unwrap(Expression expression) => expression is UnaryExpression
    {
        NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
    } convert
        ? Unwrap(convert.Operand)
        : expression;

    private static string Describe(LambdaExpression member) => member.Body.ToString();
}
