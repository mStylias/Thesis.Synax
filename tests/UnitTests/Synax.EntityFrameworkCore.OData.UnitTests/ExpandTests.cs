using Microsoft.AspNetCore.OData.Query;

namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Phase 5 — the query options a client nests inside <c>$expand</c>
/// (<c>$filter</c>/<c>$orderby</c>/<c>$top</c>/<c>$skip</c>/<c>$select</c>/<c>$count</c>/<c>$levels</c>),
/// the <c>member@odata.count</c> annotation a nested <c>$count</c> produces, and the guardrails that
/// stop <c>$expand</c> being a way around the switches that gate the same options at the top level.
/// </summary>
public class ExpandTests
{
    private static Task<ODataPage> PageAsync(string queryString, SynaxODataOptions? settings = null)
        => TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(queryString), settings);

    private static Task<ODataPage> CategoryPageAsync(string queryString, SynaxODataOptions? settings = null)
        => TestData.CategoryQuery().ToODataPageAsync(TestData.CreateRequest(queryString), settings);

    /// <summary>One product's row, projected to name plus whatever the given options expand.</summary>
    private static async Task<IDictionary<string, object?>> RowAsync(string name, string expand)
    {
        var page = await PageAsync($"?$filter=name eq '{name}'&$select=name&{expand}");
        return page.Row(0);
    }

    private static List<string> Labels(IDictionary<string, object?> row)
        => row.NestedList("tags").Cast<IDictionary<string, object?>>().Select(tag => (string)tag["label"]!).ToList();

    // ---------- Nested options ----------

    [Test]
    public async Task NestedFilter_NarrowsTheExpandedCollection()
    {
        var row = await RowAsync("Laptop", "$expand=tags($filter=label ne 'sale')");

        await Assert.That(Labels(row)).IsEquivalentTo(["refurbished", "clearance"]);
    }

    [Test]
    public async Task NestedOrderByAndTop_WindowTheExpandedCollection()
    {
        var row = await RowAsync("Laptop", "$expand=tags($orderby=label;$top=2)");

        // Ordered by label, so the window is the first two alphabetically — not the first two stored.
        await Assert.That(Labels(row)).IsEquivalentTo(["clearance", "refurbished"]);
    }

    [Test]
    public async Task NestedSkip_MovesTheWindowWithinTheExpandedCollection()
    {
        var row = await RowAsync("Laptop", "$expand=tags($orderby=label;$skip=2)");

        await Assert.That(Labels(row)).IsEquivalentTo(["sale"]);
    }

    [Test]
    public async Task NestedSelect_ProjectsTheExpandedRows()
    {
        var row = await RowAsync("Laptop", "$expand=tags($select=label;$orderby=label)");
        var first = (IDictionary<string, object?>)row.NestedList("tags")[0]!;

        await Assert.That(first.Keys).IsEquivalentTo(["label"]);
    }

    [Test]
    public async Task NestedOptions_Combine()
    {
        var row = await RowAsync(
            "Laptop", "$expand=tags($filter=label ne 'sale';$orderby=label desc;$top=1;$select=label)");

        var tags = row.NestedList("tags").Cast<IDictionary<string, object?>>().ToList();

        await Assert.That(tags).Count().IsEqualTo(1);
        await Assert.That(tags[0]["label"]).IsEqualTo("refurbished");
        await Assert.That(tags[0].Keys).IsEquivalentTo(["label"]);
    }

    [Test]
    public async Task NestedExpand_NestsAnotherLevel()
    {
        var row = await RowAsync("Laptop", "$expand=category($expand=parent)");

        await Assert.That(row.Nested("category").Nested("parent")["name"]).IsEqualTo("Hardware");
    }

    // ---------- $filter on a single-valued navigation ----------

    [Test]
    public async Task NestedFilter_OnAReferenceNavigation_KeepsOnlyTheMatchingMember()
    {
        var page = await PageAsync("?$select=name&$expand=category($filter=name eq 'Groceries')&$orderby=name");

        var byName = page.Value
            .Cast<IDictionary<string, object?>>()
            .ToDictionary(row => (string)row["name"]!, row => row["category"]);

        // Apples is in Groceries, so it keeps its category; the others come back with it emptied,
        // which is what HandleReferenceNavigationPropertyExpandFilter buys — the package's default
        // is to ignore the filter and hand back every category.
        await Assert.That(((IDictionary<string, object?>)byName["Apples"]!)["name"]).IsEqualTo("Groceries");
        await Assert.That(byName["Laptop"]).IsNull();
        await Assert.That(byName["Monitor"]).IsNull();
    }

    // ---------- Nested $count ----------

    [Test]
    public async Task NestedCount_SurfacesAsAnODataCountAnnotation()
    {
        var row = await RowAsync("Laptop", "$expand=tags($count=true)");

        await Assert.That(row["tags@odata.count"]).IsEqualTo(3L);
    }

    [Test]
    public async Task NestedCount_ReportsTheTotalBeforeTheNestedWindow()
    {
        var row = await RowAsync("Laptop", "$expand=tags($orderby=label;$top=1;$count=true)");

        await Assert.That(Labels(row)).IsEquivalentTo(["clearance"]);
        await Assert.That(row["tags@odata.count"]).IsEqualTo(3L);
    }

    [Test]
    public async Task NestedCount_CountsWhatTheNestedFilterLeft()
    {
        var row = await RowAsync("Laptop", "$expand=tags($filter=label ne 'sale';$count=true)");

        await Assert.That(row["tags@odata.count"]).IsEqualTo(2L);
    }

    [Test]
    public async Task WithoutNestedCount_NoAnnotationIsAdded()
    {
        var row = await RowAsync("Laptop", "$expand=tags");

        await Assert.That(row.Keys).IsEquivalentTo(["name", "tags"]);
    }

    // ---------- $levels ----------

    [Test]
    public async Task Levels_WalksASelfReferencingNavigation()
    {
        var page = await CategoryPageAsync("?$filter=name eq 'Electronics'&$select=name&$expand=parent($levels=2)");

        var parent = page.Row(0).Nested("parent");
        await Assert.That(parent["name"]).IsEqualTo("Hardware");
        await Assert.That(parent.Nested("parent")["name"]).IsEqualTo("Goods");
    }

    [Test]
    public async Task LevelsMax_WalksAsDeepAsMaxExpansionDepthAllows()
    {
        // Two levels of ancestry exist; a depth of 3 lets $levels=max find both and stop.
        var page = await CategoryPageAsync(
            "?$filter=name eq 'Electronics'&$select=name&$expand=parent($levels=max)",
            new SynaxODataOptions { MaxExpansionDepth = 3 });

        var parent = page.Row(0).Nested("parent");
        await Assert.That(parent["name"]).IsEqualTo("Hardware");
        await Assert.That(parent.Nested("parent")["name"]).IsEqualTo("Goods");
    }

    [Test]
    public async Task LevelsMax_StopsAtTheConfiguredDepth()
    {
        var page = await CategoryPageAsync(
            "?$filter=name eq 'Electronics'&$select=name&$expand=parent($levels=max)",
            new SynaxODataOptions { MaxExpansionDepth = 1 });

        var parent = page.Row(0).Nested("parent");
        await Assert.That(parent["name"]).IsEqualTo("Hardware");
        await Assert.That(parent.ContainsKey("parent")).IsFalse();
    }

    [Test]
    public async Task Levels_BeyondMaxExpansionDepth_IsAClientError()
    {
        Func<Task> act = () => CategoryPageAsync("?$expand=parent($levels=5)");

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("MaxExpansionDepth");
    }

    [Test]
    public async Task Levels_OnANavigationToAnotherType_IsAClientError()
    {
        Func<Task> act = () => PageAsync("?$expand=category($levels=2)", new SynaxODataOptions { MaxExpansionDepth = 5 });

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task LevelsMax_WithoutADepthLimit_IsAClientErrorNamingTheLever()
    {
        // MaxExpansionDepth = 0 is OData's "no limit", which would leave $levels=max walking the
        // graph with nothing to stop it.
        Func<Task> act = () => CategoryPageAsync(
            "?$expand=parent($levels=max)", new SynaxODataOptions { MaxExpansionDepth = 0 });

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("MaxExpansionDepth");
    }

    // ---------- Collection filters (regression) ----------

    [Test]
    public async Task Any_FiltersOnACollectionNavigation()
    {
        var page = await PageAsync("?$filter=tags/any(t: t/label eq 'sale')");

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
    }

    [Test]
    public async Task All_FiltersOnACollectionNavigation()
    {
        var page = await PageAsync("?$filter=tags/all(t: t/label ne 'sale')");

        await Assert.That(page.Names()).IsEquivalentTo(["Phone", "Apples", "Bread", "Monitor"]);
    }

    [Test]
    public async Task CollectionCount_FiltersOnTheNumberOfRelatedRows()
    {
        var page = await PageAsync("?$filter=tags/$count gt 1");

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
    }

    // ---------- Guardrails ----------

    [Test]
    [Arguments("$expand=tags($filter=label eq 'sale')", "$filter")]
    [Arguments("$expand=tags($skip=1)", "$skip")]
    [Arguments("$expand=tags($top=1)", "$top")]
    [Arguments("$expand=tags($count=true)", "$count")]
    [Arguments("$expand=tags($orderby=label)", "$orderby")]
    public async Task NestedOption_ObeysTheAllowedQueryOptions(string queryString, string option)
    {
        var settings = new SynaxODataOptions
        {
            AllowedQueryOptions = AllowedQueryOptions.Expand | AllowedQueryOptions.Select
        };

        Func<Task> act = () => PageAsync("?" + queryString, settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(option);
    }

    [Test]
    public async Task NestedSelect_ObeysTheAllowedQueryOptions()
    {
        var settings = new SynaxODataOptions { AllowedQueryOptions = AllowedQueryOptions.Expand };

        Func<Task> act = () => PageAsync("?$expand=tags($select=label)", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("$select");
    }

    [Test]
    public async Task NestedOrderBy_ObeysAllowedOrderByProperties()
    {
        var settings = new SynaxODataOptions();
        settings.AllowedOrderByProperties.Add("name");

        Func<Task> act = () => PageAsync("?$select=name&$expand=tags($orderby=label)", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("label");

        // The same restriction lets an allowed name through, nested or not.
        settings.AllowedOrderByProperties.Add("label");
        var allowed = await PageAsync("?$select=name&$expand=tags($orderby=label)", settings);
        await Assert.That(allowed.Value).Count().IsEqualTo(5);
    }

    [Test]
    public async Task NestedTop_ObeysMaxTop()
    {
        Func<Task> act = () => PageAsync("?$select=name&$expand=tags($top=50)", new SynaxODataOptions { MaxTop = 2 });

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task NestedFilter_IsBoundedByMaxNodeCount()
    {
        Func<Task> act = () => PageAsync(
            "?$select=name&$expand=tags($filter=label eq 'a' or label eq 'b' or label eq 'c')",
            new SynaxODataOptions { MaxNodeCount = 3 });

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task NestedExpand_IsBoundedByMaxExpansionDepth()
    {
        Func<Task> act = () => PageAsync(
            "?$select=name&$expand=category($expand=parent)", new SynaxODataOptions { MaxExpansionDepth = 1 });

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task NestedCompute_RequiresEnableCompute()
    {
        Func<Task> act = () => PageAsync("?$select=name&$expand=tags($compute=1 as one;$select=label,one)");

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(SynaxODataOptions.EnableCompute));
    }

    [Test]
    public async Task NestedCompute_WhenEnabled_ShowsUpInTheExpandedRows()
    {
        var page = await PageAsync(
            "?$filter=name eq 'Laptop'&$select=name&$expand=tags($compute=length(label) as size;$select=label,size;$orderby=label)",
            new SynaxODataOptions { EnableCompute = true });

        var first = (IDictionary<string, object?>)page.Row(0).NestedList("tags")[0]!;
        await Assert.That(first["size"]).IsEqualTo("clearance".Length);
    }

    [Test]
    public async Task NestedSearch_WithoutDeclaredMembers_IsAClientError()
    {
        // Without a binder the OData pipeline drops the clause and returns every expanded row —
        // the same silent match-everything the top-level guard exists to prevent.
        Func<Task> act = () => PageAsync("?$select=name&$expand=tags($search=sale)");

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(TagDto));
    }

    [Test]
    public async Task NestedSearch_ForATypeOnlyTheRootDeclared_IsAClientError()
    {
        var settings = new SynaxODataOptions();
        settings.Searchable<ProductDto>(product => product.Name);

        Func<Task> act = () => PageAsync("?$select=name&$expand=tags($search=sale)", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(TagDto));
    }

    [Test]
    public async Task NestedSearch_WithDeclaredMembers_NarrowsTheExpandedCollection()
    {
        var settings = new SynaxODataOptions();
        settings.Searchable<ProductDto>(product => product.Name);
        settings.Searchable<TagDto>(tag => tag.Label);

        var page = await PageAsync("?$filter=name eq 'Laptop'&$select=name&$expand=tags($search=clear)", settings);

        await Assert.That(Labels(page.Row(0))).IsEquivalentTo(["clearance"]);
    }

    [Test]
    public async Task NestedApply_IsAClientErrorRatherThanASilentNoOp()
    {
        Func<Task> act = () => PageAsync(
            "?$select=name&$expand=tags($apply=aggregate($count as rows))",
            new SynaxODataOptions { EnableApply = true });

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("$apply");
        await Assert.That(exception.Message).Contains("$expand");
    }
}
