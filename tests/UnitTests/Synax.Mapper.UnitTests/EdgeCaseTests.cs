using System.Linq.Expressions;
using Synax.Mapper.Core;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class EdgeCaseTests
{
    [Test]
    public async Task Map_WhenCalledMultipleTimes_ShouldReturnDistinctInstances()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var source = new SourceSimple { Id = 1, Name = "Test" };

        // Act
        var dest1 = mapper.Map<DestSimple>(source);
        var dest2 = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(ReferenceEquals(dest1, dest2)).IsFalse();
        await Assert.That(dest1.Id).IsEqualTo(dest2.Id);
    }

    [Test]
    public async Task Map_WhenCalled_ShouldNotMutateSource()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();
        var source = new SourceSimple { Id = 1, Name = "Original", Price = 10m };

        // Act
        _ = mapper.Map<DestSimple>(source);

        // Assert
        await Assert.That(source.Id).IsEqualTo(1);
        await Assert.That(source.Name).IsEqualTo("Original");
        await Assert.That(source.Price).IsEqualTo(10m);
    }

    [Test]
    public async Task GetExpression_WhenNestedMapping_ShouldHaveMemberInitBody()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<NestedProfile>();

        // Act
        var expr = mapper.GetExpression<SourceWithNav, DestWithNav>();
        var memberInit = (MemberInitExpression)expr.Body;
        var addressBinding = memberInit.Bindings
            .OfType<MemberAssignment>()
            .FirstOrDefault(b => b.Member.Name == "Address");

        // Assert
        await Assert.That(expr.Body.NodeType).IsEqualTo(ExpressionType.MemberInit);
        await Assert.That(addressBinding).IsNotNull();
        await Assert.That(addressBinding!.Expression.NodeType).IsEqualTo(ExpressionType.MemberInit);
    }

    [Test]
    public async Task GetExpression_WhenOptionalNested_ShouldHaveConditionalBody()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<NestedProfile>();

        // Act
        var expr = mapper.GetExpression<SourceWithNav, DestWithNav>();
        var memberInit = (MemberInitExpression)expr.Body;
        var optionalBinding = memberInit.Bindings
            .OfType<MemberAssignment>()
            .FirstOrDefault(b => b.Member.Name == "OptionalAddress");

        // Assert
        await Assert.That(optionalBinding).IsNotNull();
        await Assert.That(optionalBinding!.Expression.NodeType).IsEqualTo(ExpressionType.Conditional);
    }

    [Test]
    public async Task GetExpression_WhenCollectionMapping_ShouldContainSelectCall()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<CollectionListProfile>();

        // Act
        var expr = mapper.GetExpression<SourceWithCollection, DestWithCollection>();
        var memberInit = (MemberInitExpression)expr.Body;
        var itemsBinding = memberInit.Bindings
            .OfType<MemberAssignment>()
            .FirstOrDefault(b => b.Member.Name == "Items");

        // Assert
        await Assert.That(itemsBinding).IsNotNull();
        await Assert.That(itemsBinding!.Expression.NodeType).IsEqualTo(ExpressionType.Call);
    }

    [Test]
    public async Task Map_WhenMultipleTypePairs_ShouldMapEachCorrectly()
    {
        // Arrange
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<AddressProfile>();
            cfg.AddProfile<ItemProfile>();
            cfg.AddProfile<ComputedProfile>();
        });
        var mapper = config.CreateMapper();

        // Act
        var addr = mapper.Map<DestAddress>(new SourceAddress { Street = "A" });
        var item = mapper.Map<DestItem>(new SourceItem { Sku = "B" });
        var comp = mapper.Map<DestComputed>(new SourceComputed
        {
            FirstName = "C", LastName = "D", Quantity = 1, UnitPrice = 10m, Discount = 0m
        });

        // Assert
        await Assert.That(addr.Street).IsEqualTo("A");
        await Assert.That(item.Sku).IsEqualTo("B");
        await Assert.That(comp.FullName).IsEqualTo("C D");
        await Assert.That(comp.LineTotal).IsEqualTo(10m);
    }

    [Test]
    public async Task MapList_WhenCalled_ShouldProduceResultsConsistentWithMap()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ComputedProfile>();
        var sources = new List<SourceComputed>
        {
            new() { FirstName = "A", LastName = "1", Quantity = 1, UnitPrice = 10m, Discount = 0m },
            new() { FirstName = "B", LastName = "2", Quantity = 2, UnitPrice = 20m, Discount = 5m }
        };

        // Act
        var listResult = mapper.MapList<DestComputed>(sources);
        var individual0 = mapper.Map<DestComputed>(sources[0]);
        var individual1 = mapper.Map<DestComputed>(sources[1]);

        // Assert
        await Assert.That(listResult[0].FullName).IsEqualTo(individual0.FullName);
        await Assert.That(listResult[0].LineTotal).IsEqualTo(individual0.LineTotal);
        await Assert.That(listResult[1].FullName).IsEqualTo(individual1.FullName);
        await Assert.That(listResult[1].LineTotal).IsEqualTo(individual1.LineTotal);
    }
}
