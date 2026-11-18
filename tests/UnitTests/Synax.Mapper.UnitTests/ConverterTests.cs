using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class ConverterTests
{
    [Test]
    public async Task Map_WhenConverterRegistered_ShouldApplyToAutoMappedMembers()
    {
        // Arrange — DateTime → string converter declared once, applied to every match
        var mapper = MapperFactory.CreateFrom(new DateConverterProfile());
        var source = new SourceWithDates
        {
            Id = 1,
            CreatedAt = new DateTime(2026, 6, 12),
            UpdatedAt = new DateTime(2026, 6, 13)
        };

        // Act
        var dest = mapper.Map<DestWithDateStrings>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.CreatedAt).IsEqualTo("2026-06-12");
        await Assert.That(dest.UpdatedAt).IsEqualTo("2026-06-13");
    }

    [Test]
    public async Task Map_WhenNullableSourceIsNull_ShouldLiftConverterToNull()
    {
        // Arrange — DateTime? member uses the DateTime → string converter with a null guard
        var mapper = MapperFactory.CreateFrom(new DateConverterProfile());
        var source = new SourceWithDates { Id = 2, CreatedAt = new DateTime(2026, 1, 1), UpdatedAt = null };

        // Act
        var dest = mapper.Map<DestWithDateStrings>(source);

        // Assert
        await Assert.That(dest.UpdatedAt).IsNull();
    }

    [Test]
    public async Task Map_WhenCollectionElementsMatchConverter_ShouldProjectElements()
    {
        // Arrange — List<DateTime> → List<string> via the element converter, no element map
        var mapper = MapperFactory.CreateFrom(new DateConverterProfile());
        var source = new SourceWithDates
        {
            Id = 3,
            CreatedAt = new DateTime(2026, 1, 1),
            History = [new DateTime(2024, 5, 1), new DateTime(2025, 5, 1)]
        };

        // Act
        var dest = mapper.Map<DestWithDateStrings>(source);

        // Assert
        await Assert.That(dest.History).Count().IsEqualTo(2);
        await Assert.That(dest.History[0]).IsEqualTo("2024-05-01");
        await Assert.That(dest.History[1]).IsEqualTo("2025-05-01");
    }

    [Test]
    public async Task Map_WhenConverterAndExplicitForBindingMatch_ShouldApplyConverter()
    {
        // Arrange — explicit .For binding between converter-registered types
        var mapper = MapperFactory.CreateFrom(new ExplicitDateConverterProfile());
        var source = new SourceWithDates { Id = 4, CreatedAt = new DateTime(2026, 3, 15) };

        // Act
        var dest = mapper.Map<DestWithDateStrings>(source);

        // Assert
        await Assert.That(dest.CreatedAt).IsEqualTo("2026-03-15");
    }

    [Test]
    public async Task Map_WhenConverterRegisteredForIdenticalTypes_ShouldWinOverDirectAssignment()
    {
        // Arrange — a registered converter takes precedence over every other strategy
        var mapper = MapperFactory.CreateFrom(new StringDecoratorProfile());
        var source = new SourceSimple { Id = 5, Name = "Widget" };

        // Act
        var dest = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(dest.Name).IsEqualTo("Widget!");
    }

    [Test]
    public async Task ConvertUsing_WhenDuplicatePair_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        await Assert.That(() => _ = new DuplicateConverterProfile())
            .Throws<InvalidOperationException>();
    }

    private class DateConverterProfile : MapperProfile
    {
        public DateConverterProfile()
        {
            ConvertUsing<DateTime, string>(d => d.ToString("yyyy-MM-dd"));

            CreateMap<SourceWithDates, DestWithDateStrings>();
        }
    }

    private class ExplicitDateConverterProfile : MapperProfile
    {
        public ExplicitDateConverterProfile()
        {
            ConvertUsing<DateTime, string>(d => d.ToString("yyyy-MM-dd"));

            CreateMap<SourceWithDates, DestWithDateStrings>()
                .For(d => d.CreatedAt, s => s.CreatedAt);
        }
    }

    private class StringDecoratorProfile : MapperProfile
    {
        public StringDecoratorProfile()
        {
            ConvertUsing<string, string>(s => s + "!");

            CreateMap<SourceSimple, DestSimple>();
        }
    }

    private class DuplicateConverterProfile : MapperProfile
    {
        public DuplicateConverterProfile()
        {
            ConvertUsing<DateTime, string>(d => d.ToString("yyyy-MM-dd"));
            ConvertUsing<DateTime, string>(d => d.ToString("O"));
        }
    }
}
