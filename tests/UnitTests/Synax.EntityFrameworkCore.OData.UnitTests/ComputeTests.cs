namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Phase 1 — <c>$compute</c>: client-defined named expressions over the exposed shape, usable from
/// <c>$filter</c>, <c>$orderby</c> and <c>$select</c>, behind <see cref="SynaxODataOptions.EnableCompute"/>
/// and the <see cref="SynaxODataOptions.MaxNodeCount"/>/<see cref="SynaxODataOptions.AllowedComputeProperties"/>
/// guardrails.
/// </summary>
public class ComputeTests
{
    private static SynaxODataOptions Enabled() => new() { EnableCompute = true };

    [Test]
    public async Task Compute_Alone_LeavesTheResponseShapeAndTheValueInvisible()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$compute=stock mul 2 as doubled"), Enabled());

        // The element type is unchanged, so the computed value is simply not in the payload.
        await Assert.That(page.Value).Count().IsEqualTo(5);
        await Assert.That(page.Value[0]).IsTypeOf<ProductDto>();
    }

    [Test]
    public async Task Compute_IsFilterableByItsAlias()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$compute=stock mul 2 as doubled&$filter=doubled gt 20"),
                Enabled());

        await Assert.That(page.Names()).IsEquivalentTo(["Phone", "Apples"]);
    }

    [Test]
    public async Task Compute_IsOrderableByItsAlias()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$compute=price mul stock as value&$orderby=value desc&$top=2"),
                Enabled());

        await Assert.That(page.Names()).IsEquivalentTo(["Phone", "Laptop"]);
    }

    [Test]
    public async Task Compute_IsSelectableAndComesBackInTheProjectedRow()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$compute=stock mul 2 as doubled&$select=name,doubled&$orderby=name&$top=1"),
                Enabled());

        var row = page.Row(0);
        await Assert.That(row.Keys).IsEquivalentTo(["name", "doubled"]);
        await Assert.That(row["name"]).IsEqualTo("Apples");
        await Assert.That(row["doubled"]).IsEqualTo(100);
    }

    [Test]
    public async Task Compute_OverStringFunctions_Applies()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$compute=concat(name,' !') as shout&$select=shout&$orderby=name&$top=1"),
                Enabled());

        await Assert.That(page.Row(0)["shout"]).IsEqualTo("Apples !");
    }

    [Test]
    public async Task Compute_CountReflectsAFilterOverTheComputedAlias()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest(
                    "?$compute=stock mul 2 as doubled&$filter=doubled gt 20&$count=true&$top=1&$orderby=name"),
                Enabled());

        await Assert.That(page.Count).IsEqualTo(2);
        await Assert.That(page.Value).Count().IsEqualTo(1);
    }

    // ---------- Guardrails ----------

    [Test]
    public async Task Compute_NotEnabled_IsAClientError()
    {
        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$compute=stock mul 2 as doubled"));

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task Compute_OverTheNodeLimit_IsAClientError()
    {
        var settings = new SynaxODataOptions { EnableCompute = true, MaxNodeCount = 3 };

        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$compute=stock mul 2 add 3 add 4 add 5 as doubled"),
                settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(SynaxODataOptions.MaxNodeCount));
    }

    [Test]
    public async Task Compute_ReadingAnUnlistedProperty_IsAClientError()
    {
        var settings = new SynaxODataOptions { EnableCompute = true };
        settings.AllowedComputeProperties.Add("stock");

        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$compute=price mul 2 as doubled"), settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("price");
        await Assert.That(exception.Message).Contains(nameof(SynaxODataOptions.AllowedComputeProperties));
    }

    [Test]
    public async Task Compute_ReadingAListedProperty_IsAllowed()
    {
        var settings = new SynaxODataOptions { EnableCompute = true };
        settings.AllowedComputeProperties.Add("stock");

        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$compute=stock mul 2 as doubled&$filter=doubled gt 20"),
                settings);

        await Assert.That(page.Value).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Compute_OverAnUnknownProperty_IsAClientError()
    {
        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$compute=nope mul 2 as doubled"), Enabled());

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }
}
