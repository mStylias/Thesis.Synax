using Synax.Mapper.Core;
using Synax.Mapper.Exceptions;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class MapperConfigurationTests
{
    [Test]
    public async Task MapperConfiguration_WhenMultipleProfiles_ShouldNotThrow()
    {
        // Act & Assert
        await Assert.That(() =>
        {
            _ = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<AddressProfile>();
                cfg.AddProfile<ItemProfile>();
            });
        }).ThrowsNothing();
    }

    [Test]
    public async Task MapperConfiguration_WhenDuplicateMapping_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        await Assert.That(() =>
        {
            _ = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<AddressProfile>();
                cfg.AddProfile<AddressProfile>();
            });
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Map_WhenMappingNotRegistered_ShouldThrowMappingNotFoundException()
    {
        // Arrange
        var config = new MapperConfiguration(cfg => cfg.AddProfile<SimpleProfile>());
        var mapper = config.CreateMapper();

        // Act & Assert
        await Assert.That(() => mapper.Map<DestAddress>(new SourceAddress()))
            .Throws<MappingNotFoundException>();
    }

    [Test]
    public async Task MapperConfiguration_WhenForwardReferenceUsed_ShouldResolveCorrectly()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ForwardReferenceProfile>();
        var source = new SourceWithNav
        {
            Id = 1,
            Title = "Forward",
            Address = new SourceAddress { Street = "Fwd St", City = "FwdCity", Zip = "99999" }
        };

        // Act
        var dest = mapper.Map<DestWithNav>(source);

        // Assert
        await Assert.That(dest.Address.Street).IsEqualTo("Fwd St");
        await Assert.That(dest.Address.City).IsEqualTo("FwdCity");
    }

    [Test]
    public async Task MapperConfiguration_WhenProfilesRegisteredSeparately_ShouldResolveAll()
    {
        // Arrange
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile(new AddressProfile());
            cfg.AddProfile(new ItemProfile());
        });
        var mapper = config.CreateMapper();

        // Act
        var addr = mapper.Map<DestAddress>(
            new SourceAddress { Street = "A", City = "B", Zip = "C" });
        var item = mapper.Map<DestItem>(
            new SourceItem { Id = 1, Sku = "S", Quantity = 2, UnitPrice = 5m });

        // Assert
        await Assert.That(addr.Street).IsEqualTo("A");
        await Assert.That(item.Sku).IsEqualTo("S");
    }

    [Test]
    public async Task MapperConfiguration_WhenSelfReferencingMapping_ShouldThrowWithCycleError()
    {
        // Arrange — SourceCyclic.Parent / DestCyclic.Parent reference their own pair.
        // Regression: this used to recurse until StackOverflowException.
        InvalidOperationException? exception = null;

        // Act
        try
        {
            _ = new MapperConfiguration(cfg => cfg.AddProfile<CyclicProfile>());
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        // Assert
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Cyclic mapping detected");
        await Assert.That(exception.Message).Contains(nameof(SourceCyclic));
    }

    [Test]
    public async Task MapperConfiguration_WhenMutuallyCyclicMappings_ShouldThrowWithCycleError()
    {
        // Arrange — Customer → Orders → Order → Customer closes a two-map cycle
        InvalidOperationException? exception = null;

        // Act
        try
        {
            _ = new MapperConfiguration(cfg => cfg.AddProfile<MutualCyclicProfile>());
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        // Assert
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Cyclic mapping detected");
    }

    [Test]
    public async Task MapperConfiguration_WhenCycleBrokenWithIgnore_ShouldBuildAndMap()
    {
        // Arrange — same shape as the mutual cycle, but the back-reference is ignored
        var mapper = MapperFactory.CreateFrom<MutualCyclicResolvedProfile>();
        var source = new SourceCustomer
        {
            Id = 1,
            Orders = [new SourceOrder { Id = 10 }, new SourceOrder { Id = 20 }]
        };

        // Act
        var dest = mapper.Map<DestCustomer>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.Orders).Count().IsEqualTo(2);
        await Assert.That(dest.Orders[0].Id).IsEqualTo(10);
        await Assert.That(dest.Orders[1].Id).IsEqualTo(20);
        await Assert.That(dest.Orders[0].Customer).IsNull();
    }

    [Test]
    public async Task CreateMapper_WhenCalledMultipleTimes_ShouldReturnDistinctInstances()
    {
        // Arrange
        var config = new MapperConfiguration(cfg => cfg.AddProfile<SimpleProfile>());

        // Act
        var mapper1 = config.CreateMapper();
        var mapper2 = config.CreateMapper();

        // Assert
        await Assert.That(mapper1).IsNotEqualTo(mapper2);
    }
}
