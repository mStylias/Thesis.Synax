using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class UpdateMappingTests
{
    [Test]
    public async Task Map_WhenDestinationExists_ShouldUpdateInPlaceAndReturnSameInstance()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var source = new SourceSimple { Id = 42, Name = "Updated", Price = 99m, IsActive = true };
        var existing = new DestSimple { Id = 1, Name = "Stale", Price = 1m, IsActive = false };

        // Act
        var result = mapper.Map(source, existing);

        // Assert
        await Assert.That(ReferenceEquals(result, existing)).IsTrue();
        await Assert.That(existing.Id).IsEqualTo(42);
        await Assert.That(existing.Name).IsEqualTo("Updated");
        await Assert.That(existing.Price).IsEqualTo(99m);
        await Assert.That(existing.IsActive).IsTrue();
    }

    [Test]
    public async Task Map_WhenMemberIgnored_ShouldPreserveExistingValue()
    {
        // Arrange — IgnoreProfile maps Id/Name and ignores 'Ignored'
        var mapper = MapperFactory.CreateFrom<IgnoreProfile>();
        var source = new SourceSimple { Id = 7, Name = "New" };
        var existing = new DestPartial { Id = 1, Name = "Old", Ignored = "precious" };

        // Act
        mapper.Map(source, existing);

        // Assert
        await Assert.That(existing.Id).IsEqualTo(7);
        await Assert.That(existing.Name).IsEqualTo("New");
        await Assert.That(existing.Ignored).IsEqualTo("precious");
    }

    [Test]
    public async Task Map_WhenUnmappedMember_ShouldPreserveExistingValue()
    {
        // Arrange — CacheKey has no source counterpart (ignored in this profile)
        var mapper = MapperFactory.CreateFrom<AutoMapWithIgnoreProfile>();
        var source = new SourceForAutoMap { Id = 2, Name = "N", Price = 5m, Description = "D", Stock = 3 };
        var existing = new DestForAutoMap { CacheKey = "cache-123" };

        // Act
        mapper.Map(source, existing);

        // Assert
        await Assert.That(existing.Name).IsEqualTo("N");
        await Assert.That(existing.CacheKey).IsEqualTo("cache-123");
    }

    [Test]
    public async Task Map_WhenNestedMember_ShouldReplaceNestedInstance()
    {
        // Arrange — update semantics replace nested objects, they do not merge them
        var mapper = MapperFactory.CreateFrom<NestedProfile>();
        var source = new SourceWithNav
        {
            Id = 1,
            Title = "T",
            Address = new SourceAddress { Street = "New St", City = "New City", Zip = "00001" }
        };
        var oldAddress = new DestAddress { Street = "Old St" };
        var existing = new DestWithNav { Address = oldAddress };

        // Act
        mapper.Map(source, existing);

        // Assert
        await Assert.That(ReferenceEquals(existing.Address, oldAddress)).IsFalse();
        await Assert.That(existing.Address.Street).IsEqualTo("New St");
    }

    [Test]
    public async Task Map_WhenSourceIsNull_ShouldReturnDestinationUnchanged()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var existing = new DestSimple { Id = 5, Name = "Keep" };

        // Act
        var result = mapper.Map<DestSimple>(null, existing);

        // Assert
        await Assert.That(ReferenceEquals(result, existing)).IsTrue();
        await Assert.That(existing.Id).IsEqualTo(5);
        await Assert.That(existing.Name).IsEqualTo("Keep");
    }

    [Test]
    public async Task Map_WhenDestinationIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act & Assert
        await Assert.That(() => mapper.Map<DestSimple>(new SourceSimple(), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Map_WhenRuntimeSourceTypeIsDerived_ShouldUseBaseMapping()
    {
        // Arrange — proxy-shaped source updating an existing destination
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        SourceSimple source = new SourceSimpleProxy { Id = 9, Name = "Proxy" };
        var existing = new DestSimple();

        // Act
        mapper.Map(source, existing);

        // Assert
        await Assert.That(existing.Id).IsEqualTo(9);
        await Assert.That(existing.Name).IsEqualTo("Proxy");
    }
}
