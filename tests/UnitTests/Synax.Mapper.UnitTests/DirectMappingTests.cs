using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class DirectMappingTests
{
    [Test]
    public async Task Map_WhenExplicitBindings_ShouldMapAllProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var source = new SourceSimple
        {
            Id = 42,
            Name = "Widget",
            Price = 19.99m,
            CreatedAt = new DateTime(2025, 6, 15),
            IsActive = true
        };

        // Act
        var dest = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(42);
        await Assert.That(dest.Name).IsEqualTo("Widget");
        await Assert.That(dest.Price).IsEqualTo(19.99m);
        await Assert.That(dest.CreatedAt).IsEqualTo(new DateTime(2025, 6, 15));
        await Assert.That(dest.IsActive).IsTrue();
    }

    [Test]
    public async Task Map_WhenComputedStringBinding_ShouldConcatenateValues()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ComputedProfile>();
        var source = new SourceComputed
        {
            FirstName = "Jane",
            LastName = "Doe",
            Quantity = 1,
            UnitPrice = 1m,
            Discount = 0m
        };

        // Act
        var dest = mapper.Map<DestComputed>(source);

        // Assert
        await Assert.That(dest.FullName).IsEqualTo("Jane Doe");
    }

    [Test]
    public async Task Map_WhenComputedArithmeticBinding_ShouldCalculateCorrectly()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ComputedProfile>();
        var source = new SourceComputed
        {
            FirstName = "X",
            LastName = "Y",
            Quantity = 5,
            UnitPrice = 10.00m,
            Discount = 3.50m
        };

        // Act
        var dest = mapper.Map<DestComputed>(source);

        // Assert
        await Assert.That(dest.LineTotal).IsEqualTo(46.50m); // (10 * 5) - 3.5
    }

    [Test]
    public async Task Map_WhenSourceHasDefaultValues_ShouldMapDefaults()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var source = new SourceSimple();

        // Act
        var dest = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(0);
        await Assert.That(dest.Name).IsEqualTo("");
        await Assert.That(dest.Price).IsEqualTo(0m);
        await Assert.That(dest.IsActive).IsFalse();
    }

    [Test]
    public async Task Map_WhenConvertibleTypes_ShouldApplyConversion()
    {
        // Arrange — int source properties explicitly bound to long destination
        // properties; the unified For falls back to a standard conversion.
        var mapper = MapperFactory.CreateFrom(new ConvertibleProfile());
        var source = new SourceNumeric { Id = 42, Count = 7 };

        // Act
        var dest = mapper.Map<DestNumericWide>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(42L);
        await Assert.That(dest.Count).IsEqualTo(7L);
    }

    [Test]
    public async Task MapperConfiguration_WhenBindingHasNoStrategy_ShouldThrowDescriptiveError()
    {
        // Arrange — string cannot be bound to int by any strategy
        InvalidOperationException? exception = null;

        // Act
        try
        {
            _ = new MapperConfiguration(cfg => cfg.AddProfile(new UnbindableProfile()));
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        // Assert
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Cannot bind");
        await Assert.That(exception.Message).Contains(nameof(DestSimple.Id));
    }

    private class ConvertibleProfile : MapperProfile
    {
        public ConvertibleProfile()
        {
            CreateMap<SourceNumeric, DestNumericWide>()
                .For(d => d.Id, s => s.Id)
                .For(d => d.Count, s => s.Count);
        }
    }

    private class UnbindableProfile : MapperProfile
    {
        public UnbindableProfile()
        {
            CreateMap<SourceSimple, DestSimple>()
                .For(d => d.Id, s => s.Name);
        }
    }

    [Test]
    public async Task Map_WhenRuntimeTypeIsDerivedFromMappedType_ShouldUseBaseMapping()
    {
        // Arrange — SourceSimpleProxy simulates an EF Core lazy-loading proxy:
        // a runtime subclass with no mapping of its own.
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        SourceSimple source = new SourceSimpleProxy { Id = 5, Name = "Proxy", Price = 9.99m };

        // Act
        var dest = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(5);
        await Assert.That(dest.Name).IsEqualTo("Proxy");
        await Assert.That(dest.Price).IsEqualTo(9.99m);
    }

    [Test]
    public async Task Map_WhenSourceIsNull_ShouldReturnNull()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var dest = mapper.Map<DestSimple>(null);

        // Assert
        await Assert.That(dest).IsNull();
    }
}
