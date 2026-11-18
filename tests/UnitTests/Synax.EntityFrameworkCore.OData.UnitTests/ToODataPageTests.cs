namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Covers OData query application over canned in-memory queries
/// (<c>SynaxQuery.From</c>) — the same path handlers exercise against fake repositories.
/// </summary>
public class ToODataPageTests
{
    [Test]
    public async Task WithoutQueryOptions_ReturnsAllItemsUnchanged()
    {
        var page = await TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(""));

        await Assert.That(page.Value).Count().IsEqualTo(5);
        await Assert.That(page.Value[0]).IsTypeOf<ProductDto>();
        await Assert.That(page.Count).IsNull();
    }

    [Test]
    public async Task Filter_ByComparisonAndStringFunction_Applies()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=stock gt 5 and contains(name,'o')"));

        var names = page.Value.Cast<ProductDto>().Select(p => p.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Phone"]);
    }

    [Test]
    public async Task Filter_OnNestedProperty_Applies()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=category/name eq 'Groceries'"));

        var names = page.Value.Cast<ProductDto>().Select(p => p.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Apples", "Bread"]);
    }

    [Test]
    public async Task Filter_IsCaseInsensitiveOnPropertyNames()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=NAME eq 'Laptop'"));

        await Assert.That(page.Value).Count().IsEqualTo(1);
    }

    [Test]
    public async Task OrderBy_MultipleKeys_Applies()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$orderby=stock desc,name"));

        var names = page.Value.Cast<ProductDto>().Select(p => p.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Apples", "Phone", "Bread", "Laptop", "Monitor"]);
    }

    [Test]
    public async Task OrderBy_CamelCasedDateProperty_Applies()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$orderby=createdAt desc&$top=1"));

        await Assert.That(((ProductDto)page.Value[0]!).Name).IsEqualTo("Monitor");
    }

    [Test]
    public async Task TopAndSkip_WindowTheOrderedQuery()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$orderby=name&$skip=1&$top=2"));

        var names = page.Value.Cast<ProductDto>().Select(p => p.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Bread", "Laptop"]);
    }

    [Test]
    public async Task Count_ReturnsFilteredTotal_WhilePagingTheItems()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=stock gt 5&$orderby=name&$top=2&$count=true"));

        await Assert.That(page.Count).IsEqualTo(3);
        await Assert.That(page.Value).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Select_ProjectsToCamelCasedDictionaries()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$select=name,stock&$orderby=name&$top=1"));

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row.Keys).IsEquivalentTo(["name", "stock"]);
        await Assert.That(row["name"]).IsEqualTo("Apples");
    }

    [Test]
    public async Task Expand_ReturnsNestedDictionaries()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=name eq 'Laptop'&$expand=category,tags"));

        var row = (IDictionary<string, object?>)page.Value[0]!;
        var category = (IDictionary<string, object?>)row["category"]!;
        await Assert.That(category["name"]).IsEqualTo("Electronics");

        var tags = (List<object?>)row["tags"]!;
        var tag = (IDictionary<string, object?>)tags[0]!;
        await Assert.That(tag["label"]).IsEqualTo("sale");
    }

    [Test]
    public async Task InvalidFilterSyntax_ThrowsSynaxODataQueryException()
    {
        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=this is not odata"));

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task UnknownFilterProperty_ThrowsSynaxODataQueryException()
    {
        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=doesNotExist eq 3"));

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task TopAboveMaxTop_ThrowsSynaxODataQueryException()
    {
        var settings = new SynaxODataOptions { MaxTop = 2 };

        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$top=3"), settings);

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task DisallowedQueryOption_ThrowsSynaxODataQueryException()
    {
        var settings = new SynaxODataOptions
        {
            AllowedQueryOptions = Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Filter
        };

        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$orderby=name"), settings);

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task DisallowedOrderByProperty_ThrowsSynaxODataQueryException()
    {
        var settings = new SynaxODataOptions();
        settings.AllowedOrderByProperties.Add("name");

        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$orderby=stock"), settings);

        await Assert.That(act).Throws<SynaxODataQueryException>();

        var allowed = await TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$orderby=name"), settings);
        await Assert.That(allowed.Value).Count().IsEqualTo(5);
    }
}
