using Synax.Mapper.Core;
using Synax.Mapper.Exceptions;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class MappingNotFoundExceptionTests
{
    [Test]
    public async Task MappingNotFoundException_WhenCreated_ShouldContainSourceAndDestTypes()
    {
        // Act
        var ex = new MappingNotFoundException(typeof(SourceSimple), typeof(DestSimple));

        // Assert
        await Assert.That(ex.SourceType).IsEqualTo(typeof(SourceSimple));
        await Assert.That(ex.DestinationType).IsEqualTo(typeof(DestSimple));
    }

    [Test]
    public async Task MappingNotFoundException_WhenCreated_ShouldIncludeTypeNamesInMessage()
    {
        // Act
        var ex = new MappingNotFoundException(typeof(SourceSimple), typeof(DestSimple));

        // Assert
        await Assert.That(ex.Message).Contains("SourceSimple");
        await Assert.That(ex.Message).Contains("DestSimple");
    }

    [Test]
    public async Task MappingNotFoundException_WhenCreated_ShouldIncludeRegistrationHint()
    {
        // Act
        var ex = new MappingNotFoundException(typeof(SourceSimple), typeof(DestSimple));

        // Assert
        await Assert.That(ex.Message).Contains("CreateMap");
    }

    [Test]
    public async Task Map_WhenMappingNotRegistered_ShouldThrowMappingNotFoundException()
    {
        // Arrange
        var config = new MapperConfiguration(cfg => cfg.AddProfile(new EmptyProfile()));
        var mapper = config.CreateMapper();

        // Act & Assert
        await Assert.That(() => mapper.Map<DestSimple>(new SourceSimple()))
            .Throws<MappingNotFoundException>();
    }

    private class EmptyProfile : MapperProfile;
}
