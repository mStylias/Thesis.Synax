using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class MapListTests
{
    [Test]
    public async Task MapList_WhenMultipleElements_ShouldMapAll()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var sources = new List<SourceSimple>
        {
            new() { Id = 1, Name = "A", Price = 10m },
            new() { Id = 2, Name = "B", Price = 20m },
            new() { Id = 3, Name = "C", Price = 30m }
        };

        // Act
        var result = mapper.MapList<DestSimple>(sources);

        // Assert
        await Assert.That(result).Count().IsEqualTo(3);
        await Assert.That(result[0].Id).IsEqualTo(1);
        await Assert.That(result[1].Name).IsEqualTo("B");
        await Assert.That(result[2].Price).IsEqualTo(30m);
    }

    [Test]
    public async Task MapList_WhenEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var result = mapper.MapList<DestSimple>([]);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).Count().IsEqualTo(0);
    }

    [Test]
    public async Task MapList_WhenYieldEnumerable_ShouldMapAll()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        IEnumerable<SourceSimple> Generator()
        {
            yield return new SourceSimple { Id = 1, Name = "X" };
            yield return new SourceSimple { Id = 2, Name = "Y" };
        }

        // Act
        var result = mapper.MapList<DestSimple>(Generator());

        // Assert
        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result[0].Name).IsEqualTo("X");
        await Assert.That(result[1].Name).IsEqualTo("Y");
    }

    [Test]
    public async Task MapList_WhenSourceIsNull_ShouldReturnEmptyList()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var result = mapper.MapList<DestSimple>(null);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).Count().IsEqualTo(0);
    }

    [Test]
    public async Task MapList_WhenHeterogeneousSourceTypes_ShouldMapEachWithItsOwnMapping()
    {
        // Arrange — two different source types both map to DestSimple; the alt
        // mapping appends "-alt". Regression: the delegate was resolved from the
        // first element only and reused for the entire list.
        var mapper = MapperFactory.CreateFrom<HeterogeneousProfile>();
        var sources = new List<object>
        {
            new SourceSimple { Id = 1, Name = "A" },
            new SourceSimpleAlt { Id = 2, Name = "B" },
            new SourceSimple { Id = 3, Name = "C" }
        };

        // Act
        var result = mapper.MapList<DestSimple>(sources);

        // Assert
        await Assert.That(result).Count().IsEqualTo(3);
        await Assert.That(result[0].Name).IsEqualTo("A");
        await Assert.That(result[1].Name).IsEqualTo("B-alt");
        await Assert.That(result[2].Name).IsEqualTo("C");
    }

    [Test]
    public async Task MapList_WhenElementsAreDerivedTypes_ShouldUseBaseMapping()
    {
        // Arrange — mixes mapped base instances with runtime subclasses (proxy shape)
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var sources = new List<SourceSimple>
        {
            new() { Id = 1, Name = "Base" },
            new SourceSimpleProxy { Id = 2, Name = "Proxy" }
        };

        // Act
        var result = mapper.MapList<DestSimple>(sources);

        // Assert
        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result[0].Name).IsEqualTo("Base");
        await Assert.That(result[1].Name).IsEqualTo("Proxy");
    }

    [Test]
    public async Task MapList_WhenContainsNullElements_ShouldMapNullToDefault()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var sources = new List<SourceSimple>
        {
            new() { Id = 1, Name = "First" },
            null!,
            new() { Id = 3, Name = "Third" }
        };

        // Act
        var result = mapper.MapList<DestSimple>(sources);

        // Assert
        await Assert.That(result).Count().IsEqualTo(3);
        await Assert.That(result[0].Name).IsEqualTo("First");
        await Assert.That(result[1]).IsNull();
        await Assert.That(result[2].Name).IsEqualTo("Third");
    }

    [Test]
    public async Task MapList_WhenLargeArray_ShouldMapAllInOrder()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var sources = new SourceSimple[500];
        for (int i = 0; i < 500; i++)
            sources[i] = new SourceSimple { Id = i };

        // Act
        var result = mapper.MapList<DestSimple>(sources);

        // Assert
        await Assert.That(result).Count().IsEqualTo(500);
        await Assert.That(result[499].Id).IsEqualTo(499);
    }
}
