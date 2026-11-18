using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.OData.UnitTests;

public class EdmModelTests
{
    [Test]
    public async Task Models_AreCachedPerTypeAndCustomization()
    {
        var first = EdmModelCache.GetOrAdd(typeof(ProductDto), configure: null);
        var second = EdmModelCache.GetOrAdd(typeof(ProductDto), configure: null);

        await Assert.That(first).IsSameReferenceAs(second);
    }

    [Test]
    public async Task KeylessDto_WithoutConfigureModel_FailsWithDescriptiveError()
    {
        Func<Task> act = () => SynaxQueryFromKeyless()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=amount gt 1"));

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(nameof(SynaxODataOptions.ConfigureModel));
    }

    [Test]
    public async Task KeylessDto_WithConfigureModelKey_Works()
    {
        var settings = new SynaxODataOptions
        {
            ConfigureModel = builder => builder.EntityType<KeylessDto>().HasKey(d => d.Code)
        };

        var page = await SynaxQueryFromKeyless()
            .ToODataPageAsync(TestData.CreateRequest("?$filter=amount gt 1&$orderby=code"), settings);

        var codes = page.Value.Cast<KeylessDto>().Select(d => d.Code).ToList();
        await Assert.That(codes).IsEquivalentTo(["A", "B"]);
    }

    private static ISynaxQuery<KeylessDto> SynaxQueryFromKeyless()
        => SynaxQuery.From(new List<KeylessDto>
        {
            new() { Code = "B", Amount = 2 },
            new() { Code = "A", Amount = 3 },
            new() { Code = "C", Amount = 0 }
        });
}
