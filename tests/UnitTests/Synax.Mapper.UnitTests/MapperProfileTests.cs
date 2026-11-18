using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class MapperProfileTests
{
    [Test]
    public async Task CreateMap_WhenDuplicateInSameProfile_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        await Assert.That(() => _ = new DuplicateProfile())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateMap_WhenDifferentTypePairs_ShouldNotThrow()
    {
        // Act & Assert
        await Assert.That(() => _ = new MultiMappingProfile())
            .ThrowsNothing();
    }

    [Test]
    public async Task CreateMap_WhenTypePairRegistered_ShouldBeResolvable()
    {
        // Arrange
        var profile = new SingleMappingProfile();

        // Act
        var mapper = MapperFactory.CreateFrom(profile);

        // Assert
        await Assert.That(() => mapper.GetTypeMap<SourceSimple, DestSimple>())
            .ThrowsNothing();
    }

    private class DuplicateProfile : MapperProfile
    {
        public DuplicateProfile()
        {
            CreateMap<SourceSimple, DestSimple>();
            CreateMap<SourceSimple, DestSimple>();
        }
    }

    private class MultiMappingProfile : MapperProfile
    {
        public MultiMappingProfile()
        {
            CreateMap<SourceSimple, DestSimple>();
            CreateMap<SourceAddress, DestAddress>();
            CreateMap<SourceItem, DestItem>();
        }
    }

    private class SingleMappingProfile : MapperProfile
    {
        public SingleMappingProfile()
        {
            CreateMap<SourceSimple, DestSimple>();
        }
    }
}
