using System.Linq.Expressions;
using Synax.Mapper.Core.Extensions;
using Synax.Mapper.UnitTests.Profiles;
using Synax.Mapper.UnitTests.TestModels;

namespace Synax.Mapper.UnitTests;

/// <summary>
/// Verifies that every mapping feature works when the destination is a positional
/// record (a type without a parameterless constructor). These destinations are built
/// through the record's primary constructor rather than a member initializer.
/// </summary>
public class RecordMappingTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  AutoMap + explicit For
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task Map_WhenAutoMapIntoRecord_ShouldPopulateConstructorParameters()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordAutoMapProfile>();
        var source = new SourceSimple
        {
            Id = 7,
            Name = "Gadget",
            Price = 29.99m,
            CreatedAt = new DateTime(2025, 1, 1),
            IsActive = true
        };

        // Act
        var dest = mapper.Map<RecordDestSimple>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(7);
        await Assert.That(dest.Name).IsEqualTo("Gadget");
        await Assert.That(dest.Price).IsEqualTo(29.99m);
        await Assert.That(dest.CreatedAt).IsEqualTo(new DateTime(2025, 1, 1));
        await Assert.That(dest.IsActive).IsTrue();
    }

    [Test]
    public async Task Map_WhenExplicitForIntoRecord_ShouldPopulateConstructorParameters()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordExplicitProfile>();
        var source = new SourceSimple
        {
            Id = 1,
            Name = "Widget",
            Price = 4.5m,
            CreatedAt = new DateTime(2024, 6, 1),
            IsActive = false
        };

        // Act
        var dest = mapper.Map<RecordDestSimple>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.Name).IsEqualTo("Widget");
        await Assert.That(dest.Price).IsEqualTo(4.5m);
        await Assert.That(dest.CreatedAt).IsEqualTo(new DateTime(2024, 6, 1));
        await Assert.That(dest.IsActive).IsFalse();
    }

    [Test]
    public async Task Map_WhenComputedForIntoRecord_ShouldEvaluateExpressions()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordComputedProfile>();
        var source = new SourceComputed
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Quantity = 3,
            UnitPrice = 10m,
            Discount = 5m
        };

        // Act
        var dest = mapper.Map<RecordDestComputed>(source);

        // Assert
        await Assert.That(dest.FullName).IsEqualTo("Ada Lovelace");
        await Assert.That(dest.LineTotal).IsEqualTo(25m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Nested records
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task Map_WhenExplicitNestedRecord_ShouldMapChildProperties()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordNestedProfile>();
        var source = new SourceWithNav
        {
            Id = 1,
            Title = "Order #1",
            Address = new SourceAddress { Street = "100 Main", City = "Austin", Zip = "78701" }
        };

        // Act
        var dest = mapper.Map<RecordDestWithNav>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(1);
        await Assert.That(dest.Title).IsEqualTo("Order #1");
        await Assert.That(dest.Address).IsNotNull();
        await Assert.That(dest.Address.Street).IsEqualTo("100 Main");
        await Assert.That(dest.Address.City).IsEqualTo("Austin");
        await Assert.That(dest.OptionalAddress).IsNull();
    }

    [Test]
    public async Task Map_WhenOptionalNestedRecordHasValue_ShouldMap()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordNestedProfile>();
        var source = new SourceWithNav
        {
            Id = 2,
            Title = "Order #2",
            Address = new SourceAddress { Street = "A", City = "B", Zip = "C" },
            OptionalAddress = new SourceAddress { Street = "200 Oak", City = "Dallas", Zip = "75201" }
        };

        // Act
        var dest = mapper.Map<RecordDestWithNav>(source);

        // Assert
        await Assert.That(dest.OptionalAddress).IsNotNull();
        await Assert.That(dest.OptionalAddress!.Street).IsEqualTo("200 Oak");
    }

    [Test]
    public async Task Map_WhenAutoMapNestedRecord_ShouldResolveAutomatically()
    {
        // Arrange — only AutoMap(), no explicit nested binding
        var mapper = MapperFactory.CreateFrom<RecordAutoNestedProfile>();
        var source = new SourceWithNav
        {
            Id = 5,
            Title = "Auto",
            Address = new SourceAddress { Street = "1 Auto Way", City = "Reno", Zip = "00001" },
            OptionalAddress = null
        };

        // Act
        var dest = mapper.Map<RecordDestWithNav>(source);

        // Assert
        await Assert.That(dest.Address).IsNotNull();
        await Assert.That(dest.Address.Street).IsEqualTo("1 Auto Way");
        await Assert.That(dest.OptionalAddress).IsNull();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Collections into records
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task Map_WhenExplicitCollectionIntoRecord_ShouldMapElements()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordCollectionProfile>();
        var source = new SourceWithCollection
        {
            Id = 1,
            Name = "Order",
            Items =
            [
                new SourceItem { Id = 1, Sku = "A", Quantity = 2, UnitPrice = 10m },
                new SourceItem { Id = 2, Sku = "B", Quantity = 1, UnitPrice = 25m }
            ]
        };

        // Act
        var dest = mapper.Map<RecordDestWithCollection>(source);

        // Assert
        await Assert.That(dest.Items).IsNotNull();
        await Assert.That(dest.Items).Count().IsEqualTo(2);
        await Assert.That(dest.Items[0].Sku).IsEqualTo("A");
        await Assert.That(dest.Items[1].Quantity).IsEqualTo(1);
    }

    [Test]
    public async Task Map_WhenAutoMapCollectionIntoRecord_ShouldResolveAutomatically()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordAutoCollectionProfile>();
        var source = new SourceWithCollection
        {
            Id = 2,
            Name = "Auto",
            Items = [new SourceItem { Id = 9, Sku = "Z", Quantity = 4, UnitPrice = 1m }]
        };

        // Act
        var dest = mapper.Map<RecordDestWithCollection>(source);

        // Assert
        await Assert.That(dest.Items).Count().IsEqualTo(1);
        await Assert.That(dest.Items[0].Sku).IsEqualTo("Z");
    }

    [Test]
    public async Task Map_WhenCollectionMaterializesToArrayParameter_ShouldMap()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordArrayProfile>();
        var source = new SourceForArray
        {
            Id = 3,
            Items = [new SourceItem { Id = 1, Sku = "Arr", Quantity = 1, UnitPrice = 2m }]
        };

        // Act
        var dest = mapper.Map<RecordDestWithArray>(source);

        // Assert
        await Assert.That(dest.Items).IsTypeOf<RecordDestItem[]>();
        await Assert.That(dest.Items.Length).IsEqualTo(1);
        await Assert.That(dest.Items[0].Sku).IsEqualTo("Arr");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Mixed (constructor + object initializer) and Ignore
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task Map_WhenRecordHasExtraInitMember_ShouldSetViaObjectInitializer()
    {
        // Arrange — Id/Name go through the constructor, Extra through an initializer
        var mapper = MapperFactory.CreateFrom<RecordMixedProfile>();
        var source = new SourceSimple { Id = 11, Name = "Core" };

        // Act
        var dest = mapper.Map<RecordDestMixed>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(11);
        await Assert.That(dest.Name).IsEqualTo("Core");
        await Assert.That(dest.Extra).IsEqualTo("Core!");
    }

    [Test]
    public async Task Map_WhenConstructorParameterIgnored_ShouldUseDefault()
    {
        // Arrange — Ignored is excluded, so its constructor parameter receives default(string)
        var mapper = MapperFactory.CreateFrom<RecordIgnoreProfile>();
        var source = new SourceSimple { Id = 4, Name = "Kept" };

        // Act
        var dest = mapper.Map<RecordDestPartial>(source);

        // Assert
        await Assert.That(dest.Id).IsEqualTo(4);
        await Assert.That(dest.Name).IsEqualTo("Kept");
        await Assert.That(dest.Ignored).IsNull();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MapList & projection
    // ═══════════════════════════════════════════════════════════════════

    [Test]
    public async Task MapList_WhenDestinationIsRecord_ShouldMapEachElement()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordAutoMapProfile>();
        var sources = new List<SourceSimple>
        {
            new() { Id = 1, Name = "A", Price = 1m },
            new() { Id = 2, Name = "B", Price = 2m }
        };

        // Act
        var results = mapper.MapList<RecordDestSimple>(sources);

        // Assert
        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results[0].Name).IsEqualTo("A");
        await Assert.That(results[1].Id).IsEqualTo(2);
    }

    [Test]
    public async Task GetExpression_WhenPositionalRecord_ShouldProduceNewExpressionBody()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordAutoMapProfile>();

        // Act
        var expr = mapper.GetExpression<SourceSimple, RecordDestSimple>();

        // Assert — pure constructor projection (translatable by EF Core)
        await Assert.That(expr.Body).IsTypeOf<NewExpression>();
    }

    [Test]
    public async Task GetExpression_WhenPositionalRecord_ShouldNameTheMemberEachArgumentInitializes()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordAutoMapProfile>();

        // Act
        var expr = mapper.GetExpression<SourceSimple, RecordDestSimple>();

        // Assert — a NewExpression alone is not enough to be composable. `Members` is what
        // lets a consumer rewrite `new Dest(...).Name` back to the argument bound to Name.
        // EF Core's projection simplification needs it: without it, a $orderby or $filter
        // composed over the projection keeps the whole constructor call inside the ORDER BY
        // or WHERE and fails to translate. The compiler populates it for anonymous types;
        // Expression.New(ctor, args) does not.
        var body = (NewExpression)expr.Body;
        await Assert.That(body.Members).IsNotNull();
        await Assert.That(body.Members!.Select(m => m.Name).ToList())
            .IsEquivalentTo(new List<string> { "Id", "Name", "Price", "CreatedAt", "IsActive" });
    }

    [Test]
    public async Task GetExpression_WhenRecordMemberIsAnAggregate_ShouldStillNameItsMembers()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordWithCountProfile>();

        // Act
        var expr = mapper.GetExpression<SourceCustomer, RecordDestWithCount>();

        // Assert
        var body = (NewExpression)expr.Body;
        await Assert.That(body.Members).IsNotNull();
        await Assert.That(body.Members!.Select(m => m.Name).ToList())
            .IsEquivalentTo(new List<string> { "Id", "OrderCount" });
    }

    [Test]
    public async Task GetExpression_WhenRecordHasExtraInitMember_ShouldProduceMemberInitBody()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordMixedProfile>();

        // Act
        var expr = mapper.GetExpression<SourceSimple, RecordDestMixed>();

        // Assert — constructor wrapped in an object initializer for the extra member
        await Assert.That(expr.Body).IsTypeOf<MemberInitExpression>();
    }

    [Test]
    public async Task ProjectTo_WhenDestinationIsRecord_ShouldProjectThroughConstructor()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<RecordAutoMapProfile>();
        var sources = new List<SourceSimple>
        {
            new() { Id = 1, Name = "Active", Price = 10m, IsActive = true },
            new() { Id = 2, Name = "Inactive", Price = 20m, IsActive = false }
        };

        // Act
        var results = sources.AsQueryable()
            .Where(s => s.IsActive)
            .ProjectTo<RecordDestSimple>(mapper)
            .ToList();

        // Assert
        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Name).IsEqualTo("Active");
    }
}
