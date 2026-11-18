using System.Linq.Expressions;
using Synax.Mapper.Core.Extensions;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class ExpressionAndProjectionTests
{
    [Test]
    public async Task GetExpression_WhenCalled_ShouldReturnTypedExpression()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var expr = mapper.GetExpression<SourceSimple, DestSimple>();

        // Assert
        await Assert.That(expr).IsNotNull();
        await Assert.That(expr).IsTypeOf<Expression<Func<SourceSimple, DestSimple>>>();
    }

    [Test]
    public async Task GetExpression_WhenCalled_ShouldReturnMemberInitBody()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var expr = mapper.GetExpression<SourceSimple, DestSimple>();

        // Assert
        await Assert.That(expr.Body).IsTypeOf<MemberInitExpression>();
    }

    [Test]
    public async Task GetExpression_WhenCompiled_ShouldProduceSameResultAsMap()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ComputedProfile>();
        var source = new SourceComputed
        {
            FirstName = "John",
            LastName = "Smith",
            Quantity = 3,
            UnitPrice = 15m,
            Discount = 5m
        };

        // Act
        var mapped = mapper.Map<DestComputed>(source);
        var expr = mapper.GetExpression<SourceComputed, DestComputed>();
        var projected = expr.Compile()(source);

        // Assert
        await Assert.That(projected.FullName).IsEqualTo(mapped.FullName);
        await Assert.That(projected.LineTotal).IsEqualTo(mapped.LineTotal);
    }

    [Test]
    public async Task ProjectTo_WhenLinqToObjects_ShouldProjectAllElements()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var sources = new List<SourceSimple>
        {
            new() { Id = 1, Name = "A", Price = 10m, IsActive = true },
            new() { Id = 2, Name = "B", Price = 20m, IsActive = false },
            new() { Id = 3, Name = "C", Price = 30m, IsActive = true }
        };

        // Act
        var results = sources.AsQueryable()
            .ProjectTo<DestSimple>(mapper)
            .ToList();

        // Assert
        await Assert.That(results).Count().IsEqualTo(3);
        await Assert.That(results[0].Name).IsEqualTo("A");
        await Assert.That(results[2].Price).IsEqualTo(30m);
    }

    [Test]
    public async Task ProjectTo_WhenFilteredBeforeProjection_ShouldApplyFilter()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var sources = new List<SourceSimple>
        {
            new() { Id = 1, Name = "Active", Price = 10m, IsActive = true },
            new() { Id = 2, Name = "Inactive", Price = 20m, IsActive = false },
            new() { Id = 3, Name = "AlsoActive", Price = 30m, IsActive = true }
        };

        // Act
        var results = sources.AsQueryable()
            .Where(s => s.IsActive)
            .ProjectTo<DestSimple>(mapper)
            .ToList();

        // Assert
        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results[0].Name).IsEqualTo("Active");
        await Assert.That(results[1].Name).IsEqualTo("AlsoActive");
    }

    [Test]
    public async Task ProjectTo_WhenNestedMapping_ShouldProjectNestedProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<NestedProfile>();
        var sources = new List<SourceWithNav>
        {
            new()
            {
                Id = 1,
                Title = "First",
                Address = new SourceAddress { Street = "1st St", City = "CityA", Zip = "11111" }
            }
        };

        // Act
        var results = sources.AsQueryable()
            .ProjectTo<DestWithNav>(mapper)
            .ToList();

        // Assert
        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Address.Street).IsEqualTo("1st St");
    }

    [Test]
    public async Task ProjectTo_WhenCollectionMapping_ShouldProjectCollectionElements()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionListProfile>();
        var sources = new List<SourceWithCollection>
        {
            new()
            {
                Id = 1,
                Name = "Order1",
                Items = [new SourceItem { Id = 1, Sku = "X", Quantity = 1, UnitPrice = 5m }]
            }
        };

        // Act
        var results = sources.AsQueryable()
            .ProjectTo<DestWithCollection>(mapper)
            .ToList();

        // Assert
        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Items).Count().IsEqualTo(1);
        await Assert.That(results[0].Items[0].Sku).IsEqualTo("X");
    }
}
