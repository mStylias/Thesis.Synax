using System.Linq.Expressions;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Rewrites the expression the OData pipeline built from a client's query options, after
/// <c>ApplyTo</c> and before it runs — to map a comparison onto a database function the provider
/// would not choose by itself, for instance. Register implementations in DI; every OData query whose
/// <see cref="Microsoft.AspNetCore.Http.HttpRequest"/> carries those services applies them, whatever
/// <see cref="SynaxODataOptions"/> instance the endpoint passed.
/// </summary>
/// <remarks>
/// A rewriter sees only what the query options added. The query the endpoint composed — its base
/// filters, its projection — is replaced by an opaque <see cref="ParameterExpression"/> while the
/// rewriters run and put back afterwards, so handler-written LINQ is never rewritten. The count query
/// is rewritten too, so <c>$count</c> agrees with the rows. The in-memory <c>$select</c> of a
/// server-driven page is not: it runs over materialized rows, where a database function cannot.
/// </remarks>
public interface IODataExpressionRewriter
{
    Expression Rewrite(Expression expression);
}
