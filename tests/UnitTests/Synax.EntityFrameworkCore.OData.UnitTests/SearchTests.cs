namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Phase 2 — <c>$search</c> over the members an app declares with
/// <see cref="SynaxODataOptions.Searchable{T}"/>: case-insensitive containment across those members,
/// combined by the client's <c>AND</c>/<c>OR</c>/<c>NOT</c>, and a hard <c>400</c> for a type that
/// declared none (the failure mode without a binder is a silent match-everything).
/// </summary>
public class SearchTests
{
    /// <summary>Name only — the simplest declaration.</summary>
    private static SynaxODataOptions ByName() =>
        new SynaxODataOptions().Searchable<ProductDto>(product => product.Name);

    /// <summary>Name, a nullable member, and a nested one — the three shapes a chain can take.</summary>
    private static SynaxODataOptions ByNameDescriptionAndCategory() =>
        new SynaxODataOptions().Searchable<ProductDto>(
            product => product.Name,
            product => product.Description,
            product => product.Category!.Name);

    private static Task<ODataPage> SearchAsync(string search, SynaxODataOptions settings)
        => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest($"?$search={Uri.EscapeDataString(search)}"), settings);

    [Test]
    public async Task SingleTerm_MatchesAnyDeclaredMember()
    {
        var page = await SearchAsync("lap", ByName());

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
    }

    [Test]
    public async Task Term_IsCaseInsensitive()
    {
        var page = await SearchAsync("LAPTOP", ByName());

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
    }

    [Test]
    public async Task Term_MatchesAcrossAllDeclaredMembers()
    {
        // "computer" is in Description only; "Groceries" is in the nested category name only.
        var byDescription = await SearchAsync("computer", ByNameDescriptionAndCategory());
        await Assert.That(byDescription.Names()).IsEquivalentTo(["Laptop", "Phone"]);

        var byCategory = await SearchAsync("groceries", ByNameDescriptionAndCategory());
        await Assert.That(byCategory.Names()).IsEquivalentTo(["Apples", "Bread"]);
    }

    [Test]
    public async Task NullMembers_AreANonMatchNotAnException()
    {
        // Apples/Monitor have a null Description, Monitor has a null Category.
        var page = await SearchAsync("wholegrain", ByNameDescriptionAndCategory());

        await Assert.That(page.Names()).IsEquivalentTo(["Bread"]);
    }

    [Test]
    public async Task Or_UnionsTheTerms()
    {
        var page = await SearchAsync("laptop OR bread", ByName());

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop", "Bread"]);
    }

    [Test]
    public async Task And_IntersectsTheTerms()
    {
        var page = await SearchAsync("computer AND phone", ByNameDescriptionAndCategory());

        await Assert.That(page.Names()).IsEquivalentTo(["Phone"]);
    }

    [Test]
    public async Task SpaceSeparatedTerms_AreAnImplicitAnd()
    {
        var page = await SearchAsync("computer phone", ByNameDescriptionAndCategory());

        await Assert.That(page.Names()).IsEquivalentTo(["Phone"]);
    }

    [Test]
    public async Task Not_ExcludesTheTerm()
    {
        var page = await SearchAsync("NOT o", ByName());

        // Only names without an 'o': Apples, Bread.
        await Assert.That(page.Names()).IsEquivalentTo(["Apples", "Bread"]);
    }

    [Test]
    public async Task GroupedExpression_Applies()
    {
        var page = await SearchAsync("(laptop OR bread) AND NOT bread", ByName());

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
    }

    [Test]
    public async Task QuotedPhrase_IsASingleTerm()
    {
        var page = await SearchAsync("\"pocket computer\"", ByNameDescriptionAndCategory());

        await Assert.That(page.Names()).IsEquivalentTo(["Phone"]);

        // Unquoted, the same words are two ANDed terms — and still only match Phone's description.
        var unquoted = await SearchAsync("pocket computer", ByNameDescriptionAndCategory());
        await Assert.That(unquoted.Names()).IsEquivalentTo(["Phone"]);
    }

    [Test]
    public async Task WildcardCharacters_MatchLiterally()
    {
        // Bread's description is "50% wholegrain": '%' and '_' must not behave as LIKE wildcards.
        // (A term starting with a digit has to be quoted — that is the OData search lexer's rule.)
        var page = await SearchAsync("\"50% w\"", ByNameDescriptionAndCategory());
        await Assert.That(page.Names()).IsEquivalentTo(["Bread"]);

        var underscore = await SearchAsync("\"50_\"", ByNameDescriptionAndCategory());
        await Assert.That(underscore.Value).Count().IsEqualTo(0);
    }

    [Test]
    public async Task Search_ComposesWithFilterOrderAndCount()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$search=computer&$filter=stock gt 5&$orderby=name&$count=true"),
                ByNameDescriptionAndCategory());

        await Assert.That(page.Names()).IsEquivalentTo(["Phone"]);
        await Assert.That(page.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Search_CountReportsTheSearchedTotalNotTheUnsearchedOne()
    {
        var page = await TestData.ProductQuery()
            .ToODataPageAsync(
                TestData.CreateRequest("?$search=computer&$count=true&$top=1&$orderby=name"),
                ByNameDescriptionAndCategory());

        await Assert.That(page.Count).IsEqualTo(2);
        await Assert.That(page.Value).Count().IsEqualTo(1);
    }

    // ---------- Guardrails ----------

    [Test]
    public async Task UndeclaredType_IsAClientErrorNamingTheTypeAndTheApi()
    {
        // Members declared for a different type must not arm $search for this one.
        var settings = new SynaxODataOptions().Searchable<KeylessDto>(dto => dto.Code);

        Func<Task> act = () => SearchAsync("laptop", settings);

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(ProductDto));
        await Assert.That(exception.Message).Contains("Searchable");
    }

    [Test]
    public async Task NothingDeclared_IsAClientErrorNotAMatchEverything()
    {
        Func<Task> act = () => SearchAsync("laptop", new SynaxODataOptions());

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task DisallowedQueryOption_StillWinsOverTheDeclaration()
    {
        var settings = new SynaxODataOptions
        {
            AllowedQueryOptions = Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Filter
        };
        settings.Searchable<ProductDto>(product => product.Name);

        // $search is allowed by the declaration, but $filter-only settings still reject $orderby.
        Func<Task> act = () => TestData.ProductQuery()
            .ToODataPageAsync(TestData.CreateRequest("?$search=laptop&$orderby=name"), settings);

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task OptionsBuiltWithoutTheBinder_FailLoudlyRatherThanMatchingEverything()
    {
        var declared = ByName();

        // Options built against a *different* settings instance carry no search binder.
        var options = SynaxQueryODataExtensions.CreateQueryOptions<ProductDto>(
            TestData.CreateRequest("?$search=laptop"),
            new SynaxODataOptions());

        Func<Task> act = () => TestData.ProductQuery().ToODataPageAsync(options, declared);

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(nameof(SynaxQueryODataExtensions.CreateQueryOptions));
    }

    [Test]
    public async Task NonMemberSearchableDeclaration_FailsAtDeclarationTime()
    {
        var settings = new SynaxODataOptions();

        Action act = () => settings.Searchable<ProductDto>(product => product.Name.ToUpper());

        await Assert.That(act).Throws<ArgumentException>();
    }

    [Test]
    public async Task DeclaringTheSameMemberTwice_IsIdempotent()
    {
        var settings = new SynaxODataOptions()
            .Searchable<ProductDto>(product => product.Name)
            .Searchable<ProductDto>(product => product.Name);

        var page = await SearchAsync("lap", settings);

        await Assert.That(page.Names()).IsEquivalentTo(["Laptop"]);
    }
}
