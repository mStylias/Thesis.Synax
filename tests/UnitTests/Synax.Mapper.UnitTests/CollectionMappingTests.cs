using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class CollectionMappingTests
{
    private static SourceWithCollection CreateSourceWithItems() => new()
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

    [Test]
    public async Task Map_WhenCollectionTargetIsList_ShouldMaterializeToList()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionListProfile>();
        var source = CreateSourceWithItems();

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
    public async Task Map_WhenCollectionIsEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionListProfile>();
        var source = new SourceWithCollection { Id = 2, Name = "Empty", Items = [] };

        // Act
        var dest = mapper.Map<DestWithCollection>(source);

        // Assert
        await Assert.That(dest.Items).IsNotNull();
        await Assert.That(dest.Items).Count().IsEqualTo(0);
    }

    [Test]
    public async Task Map_WhenCollectionHasElements_ShouldMapAllElementProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionListProfile>();
        var source = new SourceWithCollection
        {
            Id = 1,
            Name = "Test",
            Items = [new SourceItem { Id = 99, Sku = "SKU-99", Quantity = 7, UnitPrice = 42.50m }]
        };

        // Act
        var dest = mapper.Map<DestWithCollection>(source);
        var item = dest.Items[0];

        // Assert
        await Assert.That(item.Id).IsEqualTo(99);
        await Assert.That(item.Sku).IsEqualTo("SKU-99");
        await Assert.That(item.Quantity).IsEqualTo(7);
        await Assert.That(item.UnitPrice).IsEqualTo(42.50m);
    }

    [Test]
    public async Task Map_WhenCollectionTargetIsArray_ShouldMaterializeToArray()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionArrayProfile>();
        var source = new SourceForArray
        {
            Id = 1,
            Items =
            [
                new SourceItem { Id = 1, Sku = "X", Quantity = 1, UnitPrice = 5m },
                new SourceItem { Id = 2, Sku = "Y", Quantity = 2, UnitPrice = 10m }
            ]
        };

        // Act
        var dest = mapper.Map<DestWithArray>(source);

        // Assert
        await Assert.That(dest.Items).IsNotNull();
        await Assert.That(dest.Items).Count().IsEqualTo(2);
        await Assert.That(dest.Items[0].Sku).IsEqualTo("X");
        await Assert.That(dest.Items[1].Sku).IsEqualTo("Y");
    }

    [Test]
    public async Task Map_WhenCollectionTargetIsHashSet_ShouldMaterializeToHashSet()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionHashSetProfile>();
        var source = new SourceForHashSet
        {
            Id = 1,
            Tags =
            [
                new SourceTag { Name = "alpha" },
                new SourceTag { Name = "beta" },
                new SourceTag { Name = "gamma" }
            ]
        };

        // Act
        var dest = mapper.Map<DestWithHashSet>(source);

        // Assert
        await Assert.That(dest.Tags).IsNotNull();
        await Assert.That(dest.Tags).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Map_WhenCollectionTargetIsIEnumerable_ShouldBeEnumerable()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionIEnumerableProfile>();
        var source = CreateSourceWithItems();

        // Act
        var dest = mapper.Map<DestWithIEnumerable>(source);

        // Assert
        await Assert.That(dest.Items).IsNotNull();
        var list = dest.Items.ToList();
        await Assert.That(list).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Map_WhenCollectionTargetIsIReadOnlyCollection_ShouldMaterialize()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionIReadOnlyProfile>();
        var source = CreateSourceWithItems();

        // Act
        var dest = mapper.Map<DestWithIReadOnlyCollection>(source);

        // Assert
        await Assert.That(dest.Items).IsNotNull();
        await Assert.That(dest.Items).Count().IsEqualTo(3);
    }

    [Test]
    public async Task MapperConfiguration_WhenElementMappingMissing_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        await Assert.That(() =>
        {
            _ = new MapperConfiguration(cfg => cfg.AddProfile(new MissingElementProfile()));
        }).Throws<InvalidOperationException>();
    }

    private class MissingElementProfile : MapperProfile
    {
        public MissingElementProfile()
        {
            CreateMap<SourceWithCollection, DestWithCollection>()
                .For(d => d.Id, s => s.Id)
                .For(d => d.Name, s => s.Name)
                .For(d => d.Items, s => s.Items);
        }
    }
}
