namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Phase 3 — server-driven paging: <see cref="SynaxODataOptions.PageSize"/> caps each response and
/// emits an <c>@odata.nextLink</c> carrying a keyset-shaped <c>$skiptoken</c>, which the OData
/// package's own <c>DefaultSkipTokenHandler</c> parses back. The token's wire format is a public
/// contract, so it is pinned literally here.
/// </summary>
public class ServerDrivenPagingTests
{
    private static SynaxODataOptions Paged(int pageSize = 2) => new() { PageSize = pageSize };

    private static Task<ODataPage> PageAsync(string queryString, SynaxODataOptions? settings = null)
        => TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(queryString), settings ?? Paged());

    private static Task<ODataPage> FollowAsync(string nextLink, SynaxODataOptions? settings = null)
        => TestData.ProductQuery().ToODataPageAsync(TestData.FollowLink(nextLink), settings ?? Paged());

    // ---------- Walking the pages ----------

    [Test]
    public async Task FirstPage_IsCappedAndCarriesANextLink()
    {
        var page = await PageAsync("?$orderby=name");

        await Assert.That(page.Names()).IsEquivalentTo(["Apples", "Bread"]);
        await Assert.That(page.NextLink).IsNotNull();
    }

    [Test]
    public async Task FollowingTheLink_ReturnsTheNextWindowAndTheLastPageHasNoLink()
    {
        // Apples, Bread | Laptop, Monitor | Phone
        var first = await PageAsync("?$orderby=name");
        var second = await FollowAsync(first.NextLink!);

        await Assert.That(second.Names()).IsEquivalentTo(["Laptop", "Monitor"]);
        await Assert.That(second.NextLink).IsNotNull();

        var third = await FollowAsync(second.NextLink!);
        await Assert.That(third.Names()).IsEquivalentTo(["Phone"]);
        await Assert.That(third.NextLink).IsNull();
    }

    [Test]
    public async Task Paging_WithNoOrderBy_FallsBackToTheKeyAndStillWalks()
    {
        var first = await PageAsync("");
        var second = await FollowAsync(first.NextLink!);

        await Assert.That(first.Names()).IsEquivalentTo(["Laptop", "Phone"]);
        await Assert.That(second.Names()).IsEquivalentTo(["Apples", "Bread"]);
    }

    [Test]
    public async Task Paging_HonoursADescendingOrder()
    {
        var first = await PageAsync("?$orderby=stock desc");
        var second = await FollowAsync(first.NextLink!);

        await Assert.That(first.Names()).IsEquivalentTo(["Apples", "Phone"]);
        await Assert.That(second.Names()).IsEquivalentTo(["Bread", "Laptop"]);
    }

    [Test]
    public async Task WholeCollectionSmallerThanThePage_EmitsNoLink()
    {
        var page = await PageAsync("?$orderby=name", new SynaxODataOptions { PageSize = 10 });

        await Assert.That(page.Value).Count().IsEqualTo(5);
        await Assert.That(page.NextLink).IsNull();
    }

    // ---------- The link itself ----------

    [Test]
    public async Task NextLink_PinsTheSkipTokenWireFormat()
    {
        var page = await PageAsync("?$orderby=name");

        // {edmName}-{uri literal} per stabilized $orderby clause, comma separated, url-encoded.
        await Assert.That(Uri.UnescapeDataString(page.NextLink!))
            .IsEqualTo("/api/v1/products/search?$orderby=name&$skiptoken=name-'Bread',id-00000004-0000-0000-0000-000000000000");
    }

    [Test]
    public async Task NextLink_PreservesTheClientsOtherOptionsAndDropsSkip()
    {
        var page = await PageAsync("?$filter=stock gt 2&$orderby=name&$select=name&$count=true&$skip=1&mine=keep");

        var link = Uri.UnescapeDataString(page.NextLink!);
        await Assert.That(link).Contains("$filter=stock gt 2");
        await Assert.That(link).Contains("$orderby=name");
        await Assert.That(link).Contains("$select=name");
        await Assert.That(link).Contains("$count=true");
        await Assert.That(link).Contains("mine=keep");
        await Assert.That(link).Contains("$skiptoken=");
        await Assert.That(link).DoesNotContain("$skip=");
    }

    [Test]
    public async Task NextLink_IsRelativeByDefaultAndAbsoluteWithAServiceRoot()
    {
        var relative = await PageAsync("?$orderby=name");
        await Assert.That(relative.NextLink!).StartsWith("/api/v1/products/search?");

        var absolute = await PageAsync(
            "?$orderby=name",
            new SynaxODataOptions { PageSize = 2, ServiceRoot = new Uri("https://api.example.com/") });
        await Assert.That(absolute.NextLink!).StartsWith("https://api.example.com/api/v1/products/search?");
    }

    [Test]
    public async Task SkipToken_AppliesBeforeSkip()
    {
        var first = await PageAsync("?$orderby=name");

        // Take the cursor from page 1 and add a $skip of 1 on top of it.
        var withSkip = TestData.FollowLink(first.NextLink! + "&$skip=1");
        var page = await TestData.ProductQuery().ToODataPageAsync(withSkip, Paged());

        await Assert.That(page.Names()).IsEquivalentTo(["Monitor", "Phone"]);
    }

    // ---------- $top and $count interaction ----------

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task TopAtOrBelowThePageSize_WinsAndSuppressesTheLink(int top)
    {
        var page = await PageAsync($"?$orderby=name&$top={top}");

        await Assert.That(page.Value).Count().IsEqualTo(top);
        await Assert.That(page.NextLink).IsNull();
    }

    [Test]
    public async Task TopAboveThePageSize_IsReducedPerPageSoFollowingLinksNeverExceedsIt()
    {
        var first = await PageAsync("?$orderby=name&$top=3");
        await Assert.That(first.Names()).IsEquivalentTo(["Apples", "Bread"]);
        await Assert.That(Uri.UnescapeDataString(first.NextLink!)).Contains("$top=1");

        var second = await FollowAsync(first.NextLink!);
        await Assert.That(second.Names()).IsEquivalentTo(["Laptop"]);
        await Assert.That(second.NextLink).IsNull();
    }

    [Test]
    public async Task Count_ReportsTheFullFilteredTotalNotThePage()
    {
        var page = await PageAsync("?$filter=stock gt 2&$orderby=name&$count=true");

        await Assert.That(page.Count).IsEqualTo(4);
        await Assert.That(page.Value).Count().IsEqualTo(2);
    }

    // ---------- Reshaped rows ----------

    [Test]
    public async Task Select_PagesEvenThoughTheSortKeysAreNotSelected()
    {
        var first = await PageAsync("?$select=name&$orderby=stock");

        var names = first.Value.Cast<IDictionary<string, object?>>().Select(row => (string?)row["name"]).ToList();
        await Assert.That(names).IsEquivalentTo(["Monitor", "Laptop"]);
        await Assert.That(first.Row(0).Keys).IsEquivalentTo(["name"]);
        await Assert.That(first.NextLink).IsNotNull();

        var second = await FollowAsync(first.NextLink!);
        var next = second.Value.Cast<IDictionary<string, object?>>().Select(row => (string?)row["name"]).ToList();
        await Assert.That(next).IsEquivalentTo(["Bread", "Phone"]);
    }

    [Test]
    public async Task Expand_PagesAndStillNestsTheExpandedRows()
    {
        var page = await PageAsync("?$expand=tags&$orderby=name desc");

        await Assert.That(page.NextLink).IsNotNull();

        // Phone, Monitor by name desc; neither has tags, so check the shape on the Laptop page.
        var laptopPage = await FollowAsync(page.NextLink!);
        var laptopRow = laptopPage.Value
            .Cast<IDictionary<string, object?>>()
            .Single(row => (string?)row["name"] == "Laptop");

        var tags = (List<object?>)laptopRow["tags"]!;
        await Assert.That(((IDictionary<string, object?>)tags[0]!)["label"]).IsEqualTo("sale");
    }

    // ---------- Guardrails ----------

    [Test]
    public async Task SkipToken_WithoutAPageSize_IsAClientError()
    {
        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$orderby=name&$skiptoken=name-'Bread'"));

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task NestedSortKey_CannotBePagedAndSaysSo()
    {
        Func<Task> act = () => PageAsync("?$orderby=category/name");

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("$orderby");
        await Assert.That(exception.Message).Contains("$skiptoken");
    }

    [Test]
    public async Task ComputedSortKey_CannotBePagedAndSaysSo()
    {
        var settings = new SynaxODataOptions { PageSize = 2, EnableCompute = true };

        Func<Task> act = () => PageAsync("?$compute=stock mul 2 as doubled&$orderby=doubled", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("computed");
    }

    [Test]
    public async Task MalformedSkipToken_IsAClientErrorNotAServerError()
    {
        Func<Task> act = () => PageAsync("?$orderby=name&$skiptoken=nonsense");

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task PagingOverAKeylessDto_UsesTheConfiguredKey()
    {
        var settings = new SynaxODataOptions
        {
            PageSize = 1,
            ConfigureModel = builder => builder.EntityType<KeylessDto>().HasKey(dto => dto.Code)
        };

        var query = Synax.EntityFrameworkCore.Core.SynaxQuery.From(new List<KeylessDto>
        {
            new() { Code = "A", Amount = 1 },
            new() { Code = "B", Amount = 2 }
        });

        var page = await query.ToODataPageAsync(TestData.CreateRequest("?$orderby=code"), settings);

        await Assert.That(page.Value.Cast<KeylessDto>().Single().Code).IsEqualTo("A");
        await Assert.That(Uri.UnescapeDataString(page.NextLink!)).Contains("$skiptoken=code-'A'");
    }
}
