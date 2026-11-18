using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.AspNetCore.OData.Query.Wrapper;

namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Phase 0 (enabling infrastructure): every capability beyond the original seven stays off until
/// something switches it on, the OData sub-container that carries Synax's binders is built once per
/// (element type, options), and wrapper normalization covers the dynamic rows the newer capabilities
/// produce.
/// </summary>
public class CapabilityGatingTests
{
    [Test]
    [Arguments("?$compute=stock mul 2 as dbl", "Compute")]
    [Arguments("?$search=laptop", "$search")]
    [Arguments("?$apply=groupby((name))", "Apply")]
    [Arguments("?$skiptoken=name-'Bread'", "SkipToken")]
    public async Task NotOptedIn_IsRejectedWithTheOptionNamed(string queryString, string expected)
    {
        Func<Task> act = () => TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(queryString));

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(expected);
    }

    [Test]
    public async Task Format_IsTheOneOptionSynaxAnswersItself()
    {
        // Not a capability to switch on: this bridge only ever writes JSON, so $format=json is
        // already satisfied and anything else is a 400 naming the format (see MetadataTests).
        var page = await TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest("?$format=json&$top=1"));

        await Assert.That(page.Value).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DefaultOptions_BuildNoODataContainer()
    {
        var element = new SynaxODataOptions().Elements.GetOrAdd(typeof(ProductDto));

        await Assert.That(element.Model).IsNotNull();
        await Assert.That(element.Services).IsNull();
    }

    [Test]
    public async Task DeclaringSearchableMembers_BuildsOneContainerPerElementTypeAndOptions()
    {
        var settings = new SynaxODataOptions().Searchable<ProductDto>(product => product.Name);

        var first = settings.Elements.GetOrAdd(typeof(ProductDto));
        var second = settings.Elements.GetOrAdd(typeof(ProductDto));

        await Assert.That(first).IsSameReferenceAs(second);
        await Assert.That(first.Services).IsNotNull();
        await Assert.That(first.Services!.GetService(typeof(ISearchBinder))).IsTypeOf<SynaxSearchBinder>();

        // A separate options instance is a separate cache — nothing is shared statically.
        var other = new SynaxODataOptions().Searchable<ProductDto>(product => product.Name);
        await Assert.That(other.Elements.GetOrAdd(typeof(ProductDto)).Services)
            .IsNotSameReferenceAs(first.Services);
    }

    [Test]
    public async Task Container_ResolvesTheSkipTokenHandlerTheParserNeeds()
    {
        var settings = new SynaxODataOptions().Searchable<ProductDto>(product => product.Name);

        var services = settings.Elements.GetOrAdd(typeof(ProductDto)).Services!;

        await Assert.That(services.GetService(typeof(Microsoft.AspNetCore.OData.Query.SkipTokenHandler)))
            .IsNotNull();
    }

    // ---------- Wrapper normalization ----------

    [Test]
    public async Task NormalizeValue_FlattensDynamicTypeWrappers()
    {
        var row = new FakeDynamicWrapper { ["name"] = "alpha", ["total"] = 3 };

        var normalized = (IDictionary<string, object?>)SynaxQueryODataExtensions.NormalizeValue(row)!;

        await Assert.That(normalized.Keys).IsEquivalentTo(["name", "total"]);
        await Assert.That(normalized["total"]).IsEqualTo(3);
    }

    [Test]
    public async Task NormalizeValue_RecursesThroughNestedWrappersAndCollections()
    {
        var row = new FakeDynamicWrapper
        {
            ["name"] = "alpha",
            ["nested"] = new FakeDynamicWrapper { ["count"] = 1 },
            ["items"] = new List<object?>
            {
                new FakeDynamicWrapper { ["label"] = "red" },
                new FakeDynamicWrapper { ["label"] = "blue" }
            }
        };

        var normalized = (IDictionary<string, object?>)SynaxQueryODataExtensions.NormalizeValue(row)!;

        var nested = (IDictionary<string, object?>)normalized["nested"]!;
        await Assert.That(nested["count"]).IsEqualTo(1);

        var items = (List<object?>)normalized["items"]!;
        await Assert.That(items).Count().IsEqualTo(2);
        await Assert.That(((IDictionary<string, object?>)items[1]!)["label"]).IsEqualTo("blue");
    }

    [Test]
    public async Task NormalizeValue_LeavesStringsAndBytesAlone()
    {
        await Assert.That(SynaxQueryODataExtensions.NormalizeValue("abc")).IsEqualTo("abc");
        await Assert.That(SynaxQueryODataExtensions.NormalizeValue(new byte[] { 1, 2 })).IsTypeOf<byte[]>();
        await Assert.That(SynaxQueryODataExtensions.NormalizeValue(null)).IsNull();
    }

    /// <summary>Stands in for the <c>AggregationWrapper</c> rows <c>$apply</c> produces.</summary>
    private sealed class FakeDynamicWrapper : DynamicTypeWrapper
    {
        private readonly Dictionary<string, object> _values = [];

        public override Dictionary<string, object> Values => _values;

        public object this[string key]
        {
            set => _values[key] = value;
        }
    }
}
