using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class AutoMapTests
{
    [Test]
    public async Task Map_WhenAutoMapEnabled_ShouldBindMatchingProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapProfile>();
        var source = new SourceSimple
        {
            Id = 7,
            Name = "Gadget",
            Price = 29.99m,
            CreatedAt = new DateTime(2025, 1, 1),
            IsActive = true
        };

        // Act
        var dest = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(7);
        await Assert.That(dest.Name).IsEqualTo("Gadget");
        await Assert.That(dest.Price).IsEqualTo(29.99m);
        await Assert.That(dest.CreatedAt).IsEqualTo(new DateTime(2025, 1, 1));
        await Assert.That(dest.IsActive).IsTrue();
    }

    [Test]
    public async Task Map_WhenAutoMapWithIgnoredProperties_ShouldLeaveIgnoredAtDefault()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapWithIgnoreProfile>();
        var source = new SourceForAutoMap
        {
            Id = 1,
            Name = "Test",
            Price = 5m,
            Description = "A test item",
            Stock = 100,
            InternalCode = "INTERNAL-001"
        };

        // Act
        var dest = mapper.Map<DestForAutoMap>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.Name).IsEqualTo("Test");
        await Assert.That(dest.Price).IsEqualTo(5m);
        await Assert.That(dest.Description).IsEqualTo("A test item");
        await Assert.That(dest.Stock).IsEqualTo(100);
        await Assert.That(dest.CacheKey).IsEqualTo("");
    }

    [Test]
    public async Task Map_WhenDestPropertyIsNullableOfSourceType_ShouldLiftValue()
    {
        // Arrange — int Count on the source, int? Count on the destination.
        // Regression: this used to produce invalid IL (InvalidProgramException)
        // because reflection assignability allowed a direct bind without Convert.
        var mapper = MapperFactory.CreateFrom<NullableLiftProfile>();
        var source = new SourceNumeric { Id = 42, Count = 7 };

        // Act
        var dest = mapper.Map<DestNumericNullable>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(42);
        await Assert.That(dest.Count).IsEqualTo(7);
    }

    [Test]
    public async Task Map_WhenNumericWidening_ShouldConvertAutomatically()
    {
        // Arrange — int source properties, long destination properties
        var mapper = MapperFactory.CreateFrom<AutoMapWideningProfile>();
        var source = new SourceNumeric { Id = 42, Count = 7 };

        // Act
        var dest = mapper.Map<DestNumericWide>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(42L);
        await Assert.That(dest.Count).IsEqualTo(7L);
    }

    [Test]
    public async Task Map_WhenNarrowingWouldBeRequired_ShouldSkipProperty()
    {
        // Arrange — long → int is lossy, so AutoMap must leave it unbound
        var mapper = MapperFactory.CreateFrom<AutoMapNarrowingProfile>();
        var source = new SourceWideValue { Value = long.MaxValue };

        // Act
        var dest = mapper.Map<DestNarrowValue>(source);
        var typeMap = mapper.GetTypeMap<SourceWideValue, DestNarrowValue>();

        // Assert — left at default and reported as unmapped
        await Assert.That(dest.Value).IsEqualTo(0);
        await Assert.That(typeMap.UnmappedMembers).Contains(nameof(DestNarrowValue.Value));
    }

    [Test]
    public async Task Map_WhenEnumToString_ShouldConvertViaToString()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<EnumToStringProfile>();
        var source = new SourceWithEnum
        {
            Id = 1,
            Status = SourceStatus.Active,
            OptionalStatus = SourceStatus.Closed
        };

        // Act
        var dest = mapper.Map<DestWithEnumAsString>(source);

        // Assert
        await Assert.That(dest.Status).IsEqualTo("Active");
        await Assert.That(dest.OptionalStatus).IsEqualTo("Closed");
    }

    [Test]
    public async Task Map_WhenNullableEnumToStringIsNull_ShouldMapNull()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<EnumToStringProfile>();
        var source = new SourceWithEnum { Id = 2, Status = SourceStatus.Unknown, OptionalStatus = null };

        // Act
        var dest = mapper.Map<DestWithEnumAsString>(source);

        // Assert
        await Assert.That(dest.Status).IsEqualTo("Unknown");
        await Assert.That(dest.OptionalStatus).IsNull();
    }

    [Test]
    public async Task Map_WhenEnumToEnum_ShouldConvertByValue()
    {
        // Arrange — two mirrored enum types
        var mapper = MapperFactory.CreateFrom<EnumToEnumProfile>();
        var source = new SourceWithEnum
        {
            Id = 3,
            Status = SourceStatus.Closed,
            OptionalStatus = SourceStatus.Active
        };

        // Act
        var dest = mapper.Map<DestWithEnumAsEnum>(source);

        // Assert
        await Assert.That(dest.Status).IsEqualTo(DestStatus.Closed);
        await Assert.That(dest.OptionalStatus).IsEqualTo(DestStatus.Active);
    }

    [Test]
    public async Task Map_WhenAutoMapWithIncompatibleTypes_ShouldNotThrow()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapWithIgnoreProfile>();
        var source = new SourceForAutoMap { InternalCode = "X" };

        // Act & Assert
        await Assert.That(() => mapper.Map<DestForAutoMap>(source))
            .ThrowsNothing();
    }

    [Test]
    public async Task Map_WhenAutoMapCombinedWithExplicitFor_ShouldMapAllProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AddressProfile>();
        var source = new SourceAddress
        {
            Street = "123 Elm St",
            City = "Springfield",
            Zip = "62704"
        };

        // Act
        var dest = mapper.Map<DestAddress>(source);

        // Assert
        await Assert.That(dest.Street).IsEqualTo("123 Elm St");
        await Assert.That(dest.City).IsEqualTo("Springfield");
        await Assert.That(dest.Zip).IsEqualTo("62704");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AutoMap with nested entity resolution
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task AutoMap_WithNestedMapping_ShouldResolveAutomatically()
    {
        // Arrange — only AutoMap(), no explicit nested binding
        var mapper = MapperFactory.CreateFrom<AutoMapNestedProfile>();
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
    public async Task AutoMap_WithNullableNested_ShouldReturnNullWhenSourceIsNull()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapNestedProfile>();
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
    public async Task AutoMap_WithNullableNested_ShouldMapWhenSourceHasValue()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapNestedProfile>();
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

    // ═══════════════════════════════════════════════════════════════════
    //  AutoMap with collection resolution
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task AutoMap_WithCollection_ShouldResolveAutomatically()
    {
        // Arrange — only AutoMap(), no explicit collection binding
        var mapper = MapperFactory.CreateFrom<AutoMapCollectionProfile>();
        var source = new SourceWithCollection
        {
            Id = 1,
            Name = "Order",
            Items =
            [
                new SourceItem { Id = 1, Sku = "A", Quantity = 2, UnitPrice = 10m },
                new SourceItem { Id = 2, Sku = "B", Quantity = 1, UnitPrice = 25m },
                new SourceItem { Id = 3, Sku = "C", Quantity = 5, UnitPrice = 3m }
            ]
        };

        // Act
        var dest = mapper.Map<DestWithCollection>(source);

        // Assert
        await Assert.That(dest.Items).IsNotNull();
        await Assert.That(dest.Items).Count().IsEqualTo(3);
        await Assert.That(dest.Items[0].Sku).IsEqualTo("A");
        await Assert.That(dest.Items[1].Quantity).IsEqualTo(1);
        await Assert.That(dest.Items[2].UnitPrice).IsEqualTo(3m);
    }

    [Test]
    public async Task AutoMap_WithEmptyCollection_ShouldReturnEmptyList()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapCollectionProfile>();
        var source = new SourceWithCollection { Id = 2, Name = "Empty", Items = [] };

        // Act
        var dest = mapper.Map<DestWithCollection>(source);

        // Assert
        await Assert.That(dest.Items).IsNotNull();
        await Assert.That(dest.Items).Count().IsEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AutoMap deep nesting & edge cases
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task AutoMap_WithDeepNesting_ShouldResolveAllLevels()
    {
        // Arrange — three levels, all via AutoMap()
        var mapper = MapperFactory.CreateFrom<AutoMapDeepNestedProfile>();
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
    public async Task AutoMap_WithExplicitOverride_ShouldPreferExplicit()
    {
        // Arrange — profile uses an explicit For for Address + AutoMap for the rest
        var mapper = MapperFactory.CreateFrom<AutoMapExplicitOverrideProfile>();
        var source = new SourceWithNav
        {
            Id = 1,
            Title = "Mixed",
            Address = new SourceAddress { Street = "Explicit St", City = "Override", Zip = "11111" }
        };

        // Act
        var dest = mapper.Map<DestWithNav>(source);

        // Assert — should still work correctly; explicit + auto cooperate
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.Title).IsEqualTo("Mixed");
        await Assert.That(dest.Address).IsNotNull();
        await Assert.That(dest.Address.Street).IsEqualTo("Explicit St");
    }

    [Test]
    public async Task AutoMap_WithNoRegisteredNestedMapping_ShouldSkipProperty()
    {
        // Arrange — no SourceAddress → DestAddress mapping registered
        var mapper = MapperFactory.CreateFrom(new AutoMapUnmappedNavProfile());
        var source = new SourceWithUnmappedNav
        {
            Id = 42,
            Name = "Test",
            UnmappedAddress = new SourceAddress { Street = "Hidden", City = "Ghost", Zip = "99999" }
        };

        // Act
        var dest = mapper.Map<DestWithUnmappedNav>(source);

        // Assert — primitives are mapped, nested is left at default
        await Assert.That(dest.Id).IsEqualTo(42);
        await Assert.That(dest.Name).IsEqualTo("Test");
        await Assert.That(dest.UnmappedAddress).IsNull();
    }

    private class AutoMapUnmappedNavProfile : MapperProfile
    {
        public AutoMapUnmappedNavProfile()
        {
            // Only register the parent mapping, NOT SourceAddress → DestAddress
            CreateMap<SourceWithUnmappedNav, DestWithUnmappedNav>();
        }
    }
}

