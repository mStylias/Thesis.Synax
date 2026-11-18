using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class NestedMappingTests
{
    [Test]
    public async Task Map_WhenNestedObject_ShouldMapChildProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<NestedProfile>();
        var source = new SourceWithNav
        {
            Id = 1,
            Title = "Order #1",
            Address = new SourceAddress { Street = "100 Main", City = "Austin", Zip = "78701" }
        };

        // Act
        var dest = mapper.Map<DestWithNav>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.Title).IsEqualTo("Order #1");
        await Assert.That(dest.Address).IsNotNull();
        await Assert.That(dest.Address.Street).IsEqualTo("100 Main");
        await Assert.That(dest.Address.City).IsEqualTo("Austin");
        await Assert.That(dest.Address.Zip).IsEqualTo("78701");
    }

    [Test]
    public async Task Map_WhenOptionalNestedIsNull_ShouldReturnNull()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<NestedProfile>();
        var source = new SourceWithNav
        {
            Id = 2,
            Title = "Order #2",
            Address = new SourceAddress { Street = "X", City = "Y", Zip = "Z" },
            OptionalAddress = null
        };

        // Act
        var dest = mapper.Map<DestWithNav>(source);

        // Assert
        await Assert.That(dest.OptionalAddress).IsNull();
    }

    [Test]
    public async Task Map_WhenOptionalNestedHasValue_ShouldMapChildProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<NestedProfile>();
        var source = new SourceWithNav
        {
            Id = 3,
            Title = "Order #3",
            Address = new SourceAddress { Street = "A", City = "B", Zip = "C" },
            OptionalAddress = new SourceAddress { Street = "200 Oak", City = "Dallas", Zip = "75201" }
        };

        // Act
        var dest = mapper.Map<DestWithNav>(source);

        // Assert
        await Assert.That(dest.OptionalAddress).IsNotNull();
        await Assert.That(dest.OptionalAddress!.Street).IsEqualTo("200 Oak");
        await Assert.That(dest.OptionalAddress.City).IsEqualTo("Dallas");
        await Assert.That(dest.OptionalAddress.Zip).IsEqualTo("75201");
    }

    [Test]
    public async Task Map_WhenThreeLevelsDeep_ShouldMapAllLevels()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<DeepNestedProfile>();
        var source = new SourceDeep
        {
            Id = 10,
            Nested = new SourceWithNav
            {
                Id = 20,
                Title = "Inner",
                Address = new SourceAddress { Street = "Deep St", City = "Nested City", Zip = "00000" }
            }
        };

        // Act
        var dest = mapper.Map<DestDeep>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(10);
        await Assert.That(dest.Nested).IsNotNull();
        await Assert.That(dest.Nested.Id).IsEqualTo(20);
        await Assert.That(dest.Nested.Title).IsEqualTo("Inner");
        await Assert.That(dest.Nested.Address).IsNotNull();
        await Assert.That(dest.Nested.Address.Street).IsEqualTo("Deep St");
        await Assert.That(dest.Nested.Address.City).IsEqualTo("Nested City");
    }

    [Test]
    public async Task Map_WhenNestedBoundFromComputedExpression_ShouldInlineWithNullGuard()
    {
        // Arrange — the nested mapping is fed by a coalesce expression instead of
        // a plain member access; the builder guards it defensively.
        var mapper = MapperFactory.CreateFrom(new ComputedNestedProfile());
        var withOptional = new SourceWithNav
        {
            Id = 1,
            Title = "A",
            Address = new SourceAddress { Street = "Required St" },
            OptionalAddress = new SourceAddress { Street = "Optional St" }
        };
        var withoutOptional = new SourceWithNav
        {
            Id = 2,
            Title = "B",
            Address = new SourceAddress { Street = "Required St" },
            OptionalAddress = null
        };

        // Act
        var destWithOptional = mapper.Map<DestWithNav>(withOptional);
        var destWithoutOptional = mapper.Map<DestWithNav>(withoutOptional);

        // Assert
        await Assert.That(destWithOptional.Address.Street).IsEqualTo("Optional St");
        await Assert.That(destWithoutOptional.Address.Street).IsEqualTo("Required St");
    }

    private class ComputedNestedProfile : MapperProfile
    {
        public ComputedNestedProfile()
        {
            CreateMap<SourceAddress, DestAddress>();

            CreateMap<SourceWithNav, DestWithNav>()
                .For(d => d.Address, s => s.OptionalAddress ?? s.Address);
        }
    }

    [Test]
    public async Task MapperConfiguration_WhenChildMappingMissing_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        await Assert.That(() =>
        {
            _ = new MapperConfiguration(cfg => cfg.AddProfile(new MissingChildProfile()));
        }).Throws<InvalidOperationException>();
    }

    private class MissingChildProfile : MapperProfile
    {
        public MissingChildProfile()
        {
            CreateMap<SourceWithNav, DestWithNav>()
                .For(d => d.Id, s => s.Id)
                .For(d => d.Title, s => s.Title)
                .For(d => d.Address, s => s.Address);
        }
    }
}
