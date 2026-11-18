using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

public class TypeMapTests
{
    [Test]
    public async Task GetTypeMap_WhenCalled_ShouldReturnCorrectSourceAndDestTypes()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var typeMap = mapper.GetTypeMap<SourceSimple, DestSimple>();

        // Assert
        await Assert.That(typeMap.SourceType).IsEqualTo(typeof(SourceSimple));
        await Assert.That(typeMap.DestinationType).IsEqualTo(typeof(DestSimple));
    }

    [Test]
    public async Task GetTypeMap_WhenCalled_ShouldHaveSingleParameterExpression()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var typeMap = mapper.GetTypeMap<SourceSimple, DestSimple>();

        // Assert
        await Assert.That(typeMap.Expression).IsNotNull();
        await Assert.That(typeMap.Expression.Parameters).Count().IsEqualTo(1);
    }

    [Test]
    public async Task GetExpression_WhenCalled_ShouldReturnSameInstanceAsTypeMap()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var typeMap = mapper.GetTypeMap<SourceSimple, DestSimple>();
        var directExpr = mapper.GetExpression<SourceSimple, DestSimple>();

        // Assert
        await Assert.That(ReferenceEquals(typeMap.Expression, directExpr)).IsTrue();
    }

    [Test]
    public async Task GetDelegate_WhenInvoked_ShouldProduceCorrectResult()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ComputedProfile>();
        var typeMap = mapper.GetTypeMap<SourceComputed, DestComputed>();
        var func = typeMap.GetDelegate<SourceComputed, DestComputed>();

        // Act
        var result = func(new SourceComputed
        {
            FirstName = "A",
            LastName = "B",
            Quantity = 2,
            UnitPrice = 10m,
            Discount = 1m
        });

        // Assert
        await Assert.That(result.FullName).IsEqualTo("A B");
        await Assert.That(result.LineTotal).IsEqualTo(19m);
    }

    [Test]
    public async Task GetExpression_WhenCalled_ShouldHaveParameterNamedSrc()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<SimpleProfile>();

        // Act
        var expr = mapper.GetExpression<SourceSimple, DestSimple>();

        // Assert
        await Assert.That(expr.Parameters).Count().IsEqualTo(1);
        await Assert.That(expr.Parameters[0].Name).IsEqualTo("src");
        await Assert.That(expr.Parameters[0].Type).IsEqualTo(typeof(SourceSimple));
    }
}
