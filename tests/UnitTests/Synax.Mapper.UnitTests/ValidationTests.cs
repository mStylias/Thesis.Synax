using Synax.Mapper.Core;
using Synax.Mapper.Exceptions;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class ValidationTests
{
    [Test]
    public async Task AssertConfigurationIsValid_WhenAllMembersMapped_ShouldNotThrow()
    {
        // Arrange — SourceSimple and DestSimple have identical properties
        var config = new MapperConfiguration(cfg => cfg.AddProfile<AutoMapProfile>());

        // Act & Assert
        await Assert.That(() => config.AssertConfigurationIsValid()).ThrowsNothing();
    }

    [Test]
    public async Task AssertConfigurationIsValid_WhenDestMemberUnmapped_ShouldThrowWithDetails()
    {
        // Arrange — DestForAutoMap.CacheKey has no source counterpart and is not ignored
        var config = new MapperConfiguration(cfg => cfg.AddProfile(new UnvalidatedProfile()));
        MappingValidationException? exception = null;

        // Act
        try
        {
            config.AssertConfigurationIsValid();
        }
        catch (MappingValidationException ex)
        {
            exception = ex;
        }

        // Assert
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains(nameof(DestForAutoMap.CacheKey));
        await Assert.That(exception.Message).Contains(nameof(DestForAutoMap));
        await Assert.That(exception.InvalidMaps).Count().IsEqualTo(1);
        await Assert.That(exception.InvalidMaps[0].UnmappedMembers)
            .Contains(nameof(DestForAutoMap.CacheKey));
    }

    [Test]
    public async Task AssertConfigurationIsValid_WhenMemberIgnored_ShouldNotThrow()
    {
        // Arrange — same shape as above, but CacheKey is explicitly ignored
        var config = new MapperConfiguration(cfg => cfg.AddProfile<AutoMapWithIgnoreProfile>());

        // Act & Assert
        await Assert.That(() => config.AssertConfigurationIsValid()).ThrowsNothing();
    }

    [Test]
    public async Task AssertConfigurationIsValid_WhenRecordCtorParameterUnmapped_ShouldThrow()
    {
        // Arrange — RecordDestPartial(Id, Name, Ignored): 'Ignored' has no source
        // counterpart, silently receives default, and must be flagged
        var config = new MapperConfiguration(cfg => cfg.AddProfile(new UnvalidatedRecordProfile()));
        MappingValidationException? exception = null;

        // Act
        try
        {
            config.AssertConfigurationIsValid();
        }
        catch (MappingValidationException ex)
        {
            exception = ex;
        }

        // Assert
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains(nameof(RecordDestPartial.Ignored));
    }

    [Test]
    public async Task AssertConfigurationIsValid_WhenRecordCtorParameterIgnored_ShouldNotThrow()
    {
        // Arrange — same record, but the parameter is explicitly ignored
        var config = new MapperConfiguration(cfg => cfg.AddProfile<RecordIgnoreProfile>());

        // Act & Assert
        await Assert.That(() => config.AssertConfigurationIsValid()).ThrowsNothing();
    }

    [Test]
    public async Task TypeMap_UnmappedMembers_ShouldBeQueryablePerMap()
    {
        // Arrange — the structured API for custom validation checks
        var mapper = MapperFactory.CreateFrom(new UnvalidatedProfile());

        // Act
        var typeMap = mapper.GetTypeMap<SourceForAutoMap, DestForAutoMap>();

        // Assert
        await Assert.That(typeMap.UnmappedMembers).Count().IsEqualTo(1);
        await Assert.That(typeMap.UnmappedMembers).Contains(nameof(DestForAutoMap.CacheKey));
    }

    [Test]
    public async Task TypeMap_UnmappedMembers_WhenFullyMapped_ShouldBeEmpty()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<AutoMapProfile>();

        // Act
        var typeMap = mapper.GetTypeMap<SourceSimple, DestSimple>();

        // Assert
        await Assert.That(typeMap.UnmappedMembers).Count().IsEqualTo(0);
    }

    private class UnvalidatedProfile : MapperProfile
    {
        public UnvalidatedProfile()
        {
            // CacheKey is intentionally neither mapped nor ignored
            CreateMap<SourceForAutoMap, DestForAutoMap>();
        }
    }

    private class UnvalidatedRecordProfile : MapperProfile
    {
        public UnvalidatedRecordProfile()
        {
            // 'Ignored' constructor parameter is intentionally neither mapped nor ignored
            CreateMap<SourceSimple, RecordDestPartial>();
        }
    }
}
