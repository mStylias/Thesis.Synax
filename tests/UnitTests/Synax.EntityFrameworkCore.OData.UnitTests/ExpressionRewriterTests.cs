using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// <see cref="IODataExpressionRewriter"/>s registered in DI rewrite what the query options built,
/// and only that: the endpoint's own source query is hidden from them.
/// </summary>
public class ExpressionRewriterTests
{
    [Test]
    public async Task RegisteredRewriter_RewritesTheClientFilter()
    {
        var rewriter = new CaseInsensitiveContains();

        var page = await TestData.ProductQuery().ToODataPageAsync(
            TestData.CreateRequest("?$filter=contains(name,'LAP')", Register(rewriter)));

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
    }

    [Test]
    public async Task RegisteredRewriter_NeverSeesTheEndpointsSourceQuery()
    {
        var rewriter = new CaseInsensitiveContains();
        var source = SynaxQuery.From(TestData.Products().AsQueryable().Where(product => product.Name.Contains("o")));

        var page = await source.ToODataPageAsync(
            TestData.CreateRequest("?$filter=contains(name,'LAP')", Register(rewriter)));

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
        await Assert.That(rewriter.Rewritten).IsEqualTo(1);
    }

    [Test]
    public async Task RegisteredRewriter_AppliesToTheCountToo()
    {
        var page = await TestData.ProductQuery().ToODataPageAsync(
            TestData.CreateRequest("?$filter=contains(name,'LAP')&$count=true", Register(new CaseInsensitiveContains())));

        await Assert.That(page.Count).IsEqualTo(1);
    }

    [Test]
    public async Task NoRewriter_LeavesTheQueryAsTheOptionsBuiltIt()
    {
        var page = await TestData.ProductQuery().ToODataPageAsync(
            TestData.CreateRequest("?$filter=contains(name,'LAP')"));

        await Assert.That(page.Names()).IsEmpty();
    }

    private static Action<IServiceCollection> Register(IODataExpressionRewriter rewriter)
        => services => services.AddSingleton(rewriter);

    /// <summary>Turns <c>string.Contains(string)</c> into its ordinal-ignore-case overload, and counts.</summary>
    private sealed class CaseInsensitiveContains : ExpressionVisitor, IODataExpressionRewriter
    {
        private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

        private static readonly MethodInfo ContainsIgnoringCase =
            typeof(string).GetMethod(nameof(string.Contains), [typeof(string), typeof(StringComparison)])!;

        public int Rewritten { get; private set; }

        public Expression Rewrite(Expression expression) => Visit(expression);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method != Contains)
                return base.VisitMethodCall(node);

            Rewritten++;

            return Expression.Call(
                Visit(node.Object), ContainsIgnoringCase, Visit(node.Arguments[0]),
                Expression.Constant(StringComparison.OrdinalIgnoreCase));
        }
    }
}
