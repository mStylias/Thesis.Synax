using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class IgnoreTests
{
    [Test]
    public async Task Map_WhenPropertyIgnored_ShouldLeaveAtDefault()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<IgnoreProfile>();
        var source = new SourceSimple { Id = 1, Name = "Test" };

        // Act
        var dest = mapper.Map<DestPartial>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.Name).IsEqualTo("Test");
        await Assert.That(dest.Ignored).IsEqualTo("default_value");
    }

    [Test]
    public async Task Map_WhenIgnoreWithAutoMap_ShouldNotBindIgnoredProperty()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapWithIgnoreProfile>();
        var source = new SourceForAutoMap
        {
            Id = 5,
            Name = "Product",
            Price = 99m,
            Description = "Good stuff",
            Stock = 50,
            InternalCode = "INT-001"
        };

        // Act
        var dest = mapper.Map<DestForAutoMap>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(5);
        await Assert.That(dest.Name).IsEqualTo("Product");
        await Assert.That(dest.Price).IsEqualTo(99m);
        await Assert.That(dest.Description).IsEqualTo("Good stuff");
        await Assert.That(dest.Stock).IsEqualTo(50);
        await Assert.That(dest.CacheKey).IsEqualTo("");
    }
}
