using Microsoft.Extensions.DependencyInjection;
using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class ServiceCollectionExtensionTests
{
    [Test]
    public async Task AddSynaxMapper_WhenCalled_ShouldRegisterIMapper()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg => cfg.AddProfile<SimpleProfile>());

        // Act
        var provider = services.BuildServiceProvider();
        var mapper = provider.GetService<IMapper>();

        // Assert
        await Assert.That(mapper).IsNotNull();
    }

    [Test]
    public async Task AddSynaxMapper_WhenCalled_ShouldRegisterMapperConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg => cfg.AddProfile<SimpleProfile>());

        // Act
        var provider = services.BuildServiceProvider();
        var config = provider.GetService<MapperConfiguration>();

        // Assert
        await Assert.That(config).IsNotNull();
    }

    [Test]
    public async Task AddSynaxMapper_WhenResolvedTwice_ShouldReturnSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg => cfg.AddProfile<SimpleProfile>());
        var provider = services.BuildServiceProvider();

        // Act
        var mapper1 = provider.GetRequiredService<IMapper>();
        var mapper2 = provider.GetRequiredService<IMapper>();

        // Assert
        await Assert.That(ReferenceEquals(mapper1, mapper2)).IsTrue();
    }

    [Test]
    public async Task AddSynaxMapper_WhenResolved_ShouldReturnFunctionalMapper()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg => cfg.AddProfile<SimpleProfile>());
        var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var source = new SourceSimple { Id = 42, Name = "DI Test" };

        // Act
        var dest = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(42);
        await Assert.That(dest.Name).IsEqualTo("DI Test");
    }

    [Test]
    public async Task AddSynaxMapper_WhenAssemblyHasDuplicateMappings_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        await Assert.That(() => services.AddSynaxMapper(typeof(SimpleProfile).Assembly))
            .Throws<InvalidOperationException>();
    }

    // -------- Merging across calls (modules registering their own profiles) --------

    [Test]
    public async Task AddSynaxMapper_WhenCalledTwice_ShouldResolveMappingsFromBothCalls()
    {
        // Arrange — two modules each contributing their own profiles
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg => cfg.AddProfile<AddressProfile>());
        services.AddSynaxMapper(cfg => cfg.AddProfile<ItemProfile>());
        var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        // Act
        var addr = mapper.Map<DestAddress>(
            new SourceAddress { Street = "Module A", City = "ACity", Zip = "11111" });
        var item = mapper.Map<DestItem>(
            new SourceItem { Id = 7, Sku = "MODULE-B", Quantity = 2, UnitPrice = 3m });

        // Assert — the second call must not shadow the first
        await Assert.That(addr.Street).IsEqualTo("Module A");
        await Assert.That(item.Sku).IsEqualTo("MODULE-B");
    }

    [Test]
    public async Task AddSynaxMapper_WhenCalledTwice_ShouldResolveOneConfigurationInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg => cfg.AddProfile<AddressProfile>());
        services.AddSynaxMapper(cfg => cfg.AddProfile<ItemProfile>());
        var provider = services.BuildServiceProvider();

        // Act
        var configs = provider.GetServices<MapperConfiguration>().ToArray();

        // Assert — one merged configuration, not one per call
        await Assert.That(configs).Count().IsEqualTo(1);
    }

    [Test]
    public async Task AddSynaxMapper_WhenSameAssemblyScannedTwice_ShouldNotReportDuplicateMappings()
    {
        // Arrange — two modules both scanning a shared assembly
        var services = new ServiceCollection();
        var assembly = typeof(SimpleProfile).Assembly;

        // Act & Assert — the second scan is skipped rather than colliding on every mapping.
        // (This assembly's profiles do collide with each other, so the throw that does happen
        // must come from the first scan.)
        await Assert.That(() => services.AddSynaxMapper(assembly))
            .Throws<InvalidOperationException>();
        await Assert.That(() => services.AddSynaxMapper(assembly))
            .ThrowsNothing();
    }

    [Test]
    public async Task AddSynaxMapper_WhenCalledAfterConfigurationResolved_ShouldThrowInvalidOperationException()
    {
        // Arrange — the maps are frozen once built, so a late profile would be silently ignored
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg => cfg.AddProfile<AddressProfile>());
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<MapperConfiguration>();

        // Act & Assert
        await Assert.That(() => services.AddSynaxMapper(cfg => cfg.AddProfile<ItemProfile>()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("already been built");
    }

    [Test]
    public async Task AddSynaxMapper_WhenMultipleProfiles_ShouldResolveAllMappings()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxMapper(cfg =>
        {
            cfg.AddProfile<AddressProfile>();
            cfg.AddProfile<ItemProfile>();
        });
        var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        // Act
        var addr = mapper.Map<DestAddress>(
            new SourceAddress { Street = "DI St", City = "DICity", Zip = "00000" });
        var item = mapper.Map<DestItem>(
            new SourceItem { Id = 1, Sku = "DI-SKU", Quantity = 1, UnitPrice = 1m });

        // Assert
        await Assert.That(addr.Street).IsEqualTo("DI St");
        await Assert.That(item.Sku).IsEqualTo("DI-SKU");
    }
}
