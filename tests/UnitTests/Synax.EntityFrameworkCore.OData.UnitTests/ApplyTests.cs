namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Phase 4 — <c>$apply</c>: client-driven aggregation behind
/// <see cref="SynaxODataOptions.EnableApply"/>. Covers the supported transformations, the reshaped
/// response rows, group-counting semantics for <c>$count</c>, the
/// <see cref="SynaxODataOptions.MaxNodeCount"/>/<see cref="SynaxODataOptions.MaxAggregationDepth"/>/
/// <see cref="SynaxODataOptions.AllowedAggregations"/> guardrails, and the combinations that are a
/// deliberate <c>400</c> because the OData pipeline would otherwise ignore them silently.
/// </summary>
public class ApplyTests
{
    private static SynaxODataOptions Enabled() => new() { EnableApply = true };

    private static Task<ODataPage> PageAsync(string queryString, SynaxODataOptions? settings = null)
        => TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(queryString), settings ?? Enabled());

    // ---------- Transformations ----------

    [Test]
    public async Task GroupBy_WithAggregate_ReturnsOneRowPerGroup()
    {
        var page = await PageAsync("?$apply=groupby((category/name),aggregate(stock with sum as total))");

        var groups = page.Value
            .Cast<IDictionary<string, object?>>()
            .ToDictionary(
                row => ((IDictionary<string, object?>)row["category"]!)["name"] as string ?? "(none)",
                row => row["total"]);

        await Assert.That(groups["Electronics"]).IsEqualTo(18);
        await Assert.That(groups["Groceries"]).IsEqualTo(58);
        await Assert.That(groups["(none)"]).IsEqualTo(0);
    }

    [Test]
    public async Task GroupBy_WithoutAggregate_ReturnsTheDistinctKeys()
    {
        var page = await PageAsync("?$apply=groupby((name))");

        var names = page.Value.Cast<IDictionary<string, object?>>().Select(row => (string)row["name"]!).ToList();

        await Assert.That(names).IsEquivalentTo(["Laptop", "Phone", "Apples", "Bread", "Monitor"]);
        await Assert.That(page.Row(0).Keys).IsEquivalentTo(["name"]);
    }

    [Test]
    public async Task GroupBy_OverSeveralProperties_KeysOnTheCombination()
    {
        var page = await PageAsync("?$apply=groupby((name,stock),aggregate($count as rows))");

        await Assert.That(page.Value).Count().IsEqualTo(5);
        await Assert.That(page.Row(0).Keys).IsEquivalentTo(["name", "stock", "rows"]);
    }

    [Test]
    public async Task Aggregate_WithoutGroupBy_ReturnsASingleTotalRow()
    {
        var page = await PageAsync("?$apply=aggregate(stock with sum as total,$count as rows)");

        await Assert.That(page.Value).Count().IsEqualTo(1);
        await Assert.That(page.Row(0)["total"]).IsEqualTo(76);
        await Assert.That(page.Row(0)["rows"]).IsEqualTo(5L);
    }

    [Test]
    public async Task Aggregate_SupportsMinMaxAverageAndCountDistinct()
    {
        var page = await PageAsync(
            "?$apply=aggregate(price with min as cheapest,price with max as dearest," +
            "price with average as mean,name with countdistinct as names)");

        var row = page.Row(0);
        await Assert.That(row["cheapest"]).IsEqualTo(2m);
        await Assert.That(row["dearest"]).IsEqualTo(1200m);
        await Assert.That(row["mean"]).IsEqualTo(461.1m);
        await Assert.That(row["names"]).IsEqualTo(5L);
    }

    [Test]
    public async Task Filter_Transformation_NarrowsRowsAndKeepsTheQueriedType()
    {
        var page = await PageAsync("?$apply=filter(stock gt 5)&$orderby=name");

        // Nothing reshaped the rows, so they are still the DTO — not dictionaries.
        await Assert.That(page.Names()).IsEquivalentTo(["Apples", "Bread", "Phone"]);
    }

    [Test]
    public async Task Filter_ThenGroupBy_AppliesInChainOrder()
    {
        var page = await PageAsync(
            "?$apply=filter(stock gt 5)/groupby((category/name),aggregate($count as rows))");

        var rows = page.Value
            .Cast<IDictionary<string, object?>>()
            .ToDictionary(
                row => ((IDictionary<string, object?>)row["category"]!)["name"] as string ?? "(none)",
                row => row["rows"]);

        await Assert.That(rows["Electronics"]).IsEqualTo(1L);
        await Assert.That(rows["Groceries"]).IsEqualTo(2L);
    }

    [Test]
    public async Task Compute_Transformation_FeedsTheAggregation()
    {
        var page = await PageAsync(
            "?$apply=compute(price mul stock as value)/groupby((category/name),aggregate(value with sum as revenue))");

        var rows = page.Value
            .Cast<IDictionary<string, object?>>()
            .ToDictionary(
                row => ((IDictionary<string, object?>)row["category"]!)["name"] as string ?? "(none)",
                row => row["revenue"]);

        // Laptop 3 * 1200 + Phone 15 * 800 = 15600
        await Assert.That(rows["Electronics"]).IsEqualTo(15600m);
    }

    [Test]
    public async Task Compute_Transformation_Alone_ReturnsTheEntityMembersPlusTheComputedOnes()
    {
        var page = await PageAsync("?$apply=compute(price mul stock as value)");

        var row = page.Row(0);
        await Assert.That(row["value"]).IsEqualTo(3600m);
        await Assert.That(row["name"]).IsEqualTo("Laptop");
    }

    // ---------- Composition with the other options ----------

    [Test]
    public async Task Aggregation_IsFilterableAndOrderableByAlias()
    {
        var page = await PageAsync(
            "?$apply=groupby((name),aggregate(stock with sum as total))&$filter=total gt 5&$orderby=total desc");

        var names = page.Value.Cast<IDictionary<string, object?>>().Select(row => (string)row["name"]!).ToList();

        await Assert.That(names).IsEquivalentTo(["Apples", "Phone", "Bread"]);
    }

    [Test]
    public async Task Aggregation_HonoursTopAndSkip()
    {
        var page = await PageAsync("?$apply=groupby((name))&$orderby=name&$skip=1&$top=2");

        var names = page.Value.Cast<IDictionary<string, object?>>().Select(row => (string)row["name"]!).ToList();

        await Assert.That(names).IsEquivalentTo(["Bread", "Laptop"]);
    }

    [Test]
    public async Task Count_ReportsTheNumberOfGroups_NotOfSourceRows()
    {
        var page = await PageAsync("?$apply=groupby((category/name))&$count=true");

        await Assert.That(page.Value).Count().IsEqualTo(3);
        await Assert.That(page.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Count_WithAFilteringApply_ReportsTheNarrowedTotal()
    {
        var page = await PageAsync("?$apply=filter(stock gt 5)&$count=true");

        await Assert.That(page.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Count_IsUnaffectedByPagingOverAnAggregation()
    {
        var page = await PageAsync(
            "?$apply=groupby((name))&$orderby=name&$count=true",
            new SynaxODataOptions { EnableApply = true, PageSize = 2 });

        await Assert.That(page.Value).Count().IsEqualTo(2);
        await Assert.That(page.Count).IsEqualTo(5);
    }

    // ---------- Server-driven paging over reshaped rows ----------

    [Test]
    public async Task Paging_AnAggregation_UsesASkipOffsetRatherThanACursor()
    {
        var settings = new SynaxODataOptions { EnableApply = true, PageSize = 2 };

        var first = await PageAsync("?$apply=groupby((name))&$orderby=name", settings);

        await Assert.That(Names(first)).IsEquivalentTo(["Apples", "Bread"]);
        await Assert.That(first.NextLink).Contains("$skip=2");
        await Assert.That(first.NextLink).DoesNotContain("$skiptoken");
    }

    [Test]
    public async Task Paging_AnAggregation_WalksEveryGroup()
    {
        var settings = new SynaxODataOptions { EnableApply = true, PageSize = 2 };

        var first = await PageAsync("?$apply=groupby((name))&$orderby=name", settings);
        var second = await FollowAsync(first.NextLink!, settings);
        var third = await FollowAsync(second.NextLink!, settings);

        await Assert.That(Names(second)).IsEquivalentTo(["Laptop", "Monitor"]);
        await Assert.That(Names(third)).IsEquivalentTo(["Phone"]);
        await Assert.That(third.NextLink).IsNull();
    }

    [Test]
    public async Task Paging_AnAggregation_ReducesTheClientTopPerPage()
    {
        var settings = new SynaxODataOptions { EnableApply = true, PageSize = 2 };

        var first = await PageAsync("?$apply=groupby((name))&$orderby=name&$top=3", settings);
        await Assert.That(first.NextLink).Contains("$top=1");

        var second = await FollowAsync(first.NextLink!, settings);
        await Assert.That(Names(second)).IsEquivalentTo(["Laptop"]);
        await Assert.That(second.NextLink).IsNull();
    }

    [Test]
    public async Task Paging_AnAggregationThatFitsOnOnePage_EmitsNoLink()
    {
        var settings = new SynaxODataOptions { EnableApply = true, PageSize = 25 };

        var page = await PageAsync("?$apply=groupby((category/name))", settings);

        await Assert.That(page.Value).Count().IsEqualTo(3);
        await Assert.That(page.NextLink).IsNull();
    }

    [Test]
    public async Task Paging_AFilteringApply_StillUsesTheKeysetCursor()
    {
        var settings = new SynaxODataOptions { EnableApply = true, PageSize = 2 };

        var first = await PageAsync("?$apply=filter(stock gt 1)&$orderby=name", settings);

        await Assert.That(first.Names()).IsEquivalentTo(["Apples", "Bread"]);
        await Assert.That(first.NextLink).Contains("$skiptoken=name-");

        var second = await FollowAsync(first.NextLink!, settings);
        await Assert.That(second.Names()).IsEquivalentTo(["Laptop", "Phone"]);
    }

    // ---------- Guardrails ----------

    [Test]
    public async Task NodeCount_BoundsTheWholeTransformationChain()
    {
        var settings = new SynaxODataOptions { EnableApply = true, MaxNodeCount = 4 };

        Func<Task> act = () => PageAsync(
            "?$apply=filter(stock gt 1 and price gt 2 and name ne 'x')/groupby((name))", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("MaxNodeCount");
    }

    [Test]
    public async Task AggregationDepth_BoundsHowDeeplyTransformationsNest()
    {
        var settings = new SynaxODataOptions { EnableApply = true, MaxAggregationDepth = 2 };

        // filter / groupby+aggregate is three transformations deep.
        Func<Task> act = () => PageAsync(
            "?$apply=filter(stock gt 1)/groupby((name),aggregate(stock with sum as total))", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("MaxAggregationDepth");

        // ...while the chain one shallower is fine.
        var page = await PageAsync("?$apply=groupby((name),aggregate(stock with sum as total))", settings);
        await Assert.That(page.Value).Count().IsEqualTo(5);
    }

    [Test]
    public async Task AllowedAggregations_RestrictsTheMethodsClientsMayUse()
    {
        var settings = new SynaxODataOptions { EnableApply = true };
        settings.AllowedAggregations.Add("sum");
        settings.AllowedAggregations.Add("$count");

        var allowed = await PageAsync("?$apply=aggregate(stock with sum as total)", settings);
        await Assert.That(allowed.Row(0)["total"]).IsEqualTo(76);

        Func<Task> act = () => PageAsync("?$apply=aggregate(price with max as dearest)", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("max");
        await Assert.That(exception.Message).Contains("AllowedAggregations");
    }

    [Test]
    public async Task ExpandTransformation_IsRejectedRatherThanSilentlyIgnored()
    {
        Func<Task> act = () => PageAsync("?$apply=expand(tags,filter(label eq 'sale'))");

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("expand");
    }

    [Test]
    public async Task ComputeQueryOption_CannotBeCombinedWithApply()
    {
        var settings = new SynaxODataOptions { EnableApply = true, EnableCompute = true };

        Func<Task> act = () => PageAsync("?$compute=stock mul 2 as dbl&$apply=groupby((name))", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("$compute");
        await Assert.That(exception.Message).Contains("compute(");
    }

    [Test]
    [Arguments("$select=name")]
    [Arguments("$expand=category")]
    [Arguments("$skiptoken=name-'Bread'")]
    public async Task ReshapingApply_RejectsOptionsThatCannotApplyToAnAggregation(string option)
    {
        var settings = new SynaxODataOptions { EnableApply = true, PageSize = 2 };

        Func<Task> act = () => PageAsync(
            $"?$apply=groupby((name),aggregate(stock with sum as total))&{option}", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(option.Split('=')[0]);
        // The message says what the rows actually contain.
        await Assert.That(exception.Message).Contains("name, total");
    }

    [Test]
    public async Task ReshapingApply_RejectsSearch()
    {
        var settings = new SynaxODataOptions { EnableApply = true }
            .Searchable<ProductDto>(product => product.Name);

        Func<Task> act = () => PageAsync("?$apply=groupby((name))&$search=laptop", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("$search");
    }

    [Test]
    public async Task FilteringApply_StillAllowsSelectAndSearch()
    {
        var settings = new SynaxODataOptions { EnableApply = true }
            .Searchable<ProductDto>(product => product.Name);

        var selected = await PageAsync("?$apply=filter(stock gt 5)&$select=name&$orderby=name", settings);
        await Assert.That(selected.Row(0).Keys).IsEquivalentTo(["name"]);
        await Assert.That(selected.Row(0)["name"]).IsEqualTo("Apples");

        var searched = await PageAsync("?$apply=filter(stock gt 5)&$search=apples", settings);
        await Assert.That(searched.Names()).IsEquivalentTo(["Apples"]);
    }

    [Test]
    public async Task AllowedOrderByProperties_CannotBeEnforcedOverAnAggregation_SoThePairIsRejected()
    {
        var settings = new SynaxODataOptions { EnableApply = true };
        settings.AllowedOrderByProperties.Add("name");

        Func<Task> act = () => PageAsync("?$apply=groupby((name))&$orderby=name", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("AllowedOrderByProperties");
    }

    [Test]
    public async Task OrderingByAMemberTheApplyDroppedNamesWhatTheRowsActuallyHave()
    {
        Func<Task> act = () => PageAsync(
            "?$apply=groupby((name),aggregate(stock with sum as total))&$orderby=price");

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("name, total");
    }

    [Test]
    public async Task NotEnabled_IsAClientErrorNamingTheOption()
    {
        Func<Task> act = () => PageAsync("?$apply=groupby((name))", new SynaxODataOptions());

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("Apply");
    }

    [Test]
    public async Task UnsupportedTransformationSyntax_IsAClientError()
    {
        // topcount/topsum/bottomcount are not in the package's $apply grammar at all.
        Func<Task> act = () => PageAsync("?$apply=topcount(2,price)");

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    // ---------- Helpers ----------

    private static Task<ODataPage> FollowAsync(string nextLink, SynaxODataOptions settings)
        => TestData.ProductQuery().ToODataPageAsync(TestData.FollowLink(nextLink), settings);

    private static List<string> Names(ODataPage page)
        => page.Value.Cast<IDictionary<string, object?>>().Select(row => (string)row["name"]!).ToList();
}
