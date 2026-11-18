using System.Linq.Expressions;
using Synax.Mapper.Core;
using Synax.Mapper.Core.Extensions;

namespace Synax.Mapper.UnitTests;

public class MapParameterTests
{
    private static readonly List<Product> Products =
    [
        new()
        {
            Id = 1,
            Names = [new() { Language = "el", Text = "Κούρεμα" }, new() { Language = "en", Text = "Haircut" }]
        },
        new() { Id = 2, Names = [new() { Language = "el", Text = "Ξύρισμα" }] }
    ];

    [Test]
    public async Task ProjectTo_WhenTheParameterIsSupplied_ShouldReadItsValue()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();

        // Act
        var english = Products.AsQueryable()
            .ProjectTo<ProductDto>(mapper, new MapParameters { ["language"] = "en" })
            .ToList();
        var greek = Products.AsQueryable()
            .ProjectTo<ProductDto>(mapper, new MapParameters { ["language"] = "el" })
            .ToList();

        // Assert
        await Assert.That(english.Select(p => p.Name)).IsEquivalentTo(["Haircut", "Ξύρισμα"]);
        await Assert.That(greek.Select(p => p.Name)).IsEquivalentTo(["Κούρεμα", "Ξύρισμα"]);
    }

    [Test]
    public async Task ProjectTo_WhenANestedMapReadsTheParameter_ShouldBindItInsideTheParent()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();
        var orders = new List<Order> { new() { Id = 7, Lines = [Products[0]] } };

        // Act
        var order = orders.AsQueryable()
            .ProjectTo<OrderDto>(mapper, new MapParameters { ["language"] = "en" })
            .Single();

        // Assert
        await Assert.That(order.Lines.Single().Name).IsEqualTo("Haircut");
        await Assert.That(mapper.GetTypeMap<Order, OrderDto>().ParameterNames).IsEquivalentTo(["language"]);
    }

    [Test]
    public async Task ProjectTo_WhenTheParameterIsMissing_ShouldThrowNamingIt()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();

        // Act & Assert
        await Assert.That(() => Products.AsQueryable().ProjectTo<ProductDto>(mapper))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'language'");
    }

    [Test]
    public async Task ProjectTo_WhenAnUnknownParameterIsSupplied_ShouldThrowNamingIt()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();
        var parameters = new MapParameters { ["language"] = "en", ["langauge"] = "el" };

        // Act & Assert
        await Assert.That(() => Products.AsQueryable().ProjectTo<ProductDto>(mapper, parameters))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'langauge'");
    }

    [Test]
    public async Task ProjectTo_WhenTheValueHasAnotherType_ShouldThrow()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();

        // Act & Assert
        await Assert.That(() => Products.AsQueryable()
                .ProjectTo<ProductDto>(mapper, new MapParameters { ["language"] = 42 }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ProjectTo_WhenANullableParameterIsNull_ShouldBindNull()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<FlagProfile>();

        // Act
        var flagged = Products.AsQueryable()
            .ProjectTo<FlagDto>(mapper, new MapParameters { ["preferredId"] = null })
            .ToList();

        // Assert
        await Assert.That(flagged.Any(p => p.IsPreferred)).IsFalse();
    }

    [Test]
    public async Task ProjectTo_WhenAValueTypeParameterIsNull_ShouldThrow()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ThresholdProfile>();

        // Act & Assert
        await Assert.That(() => Products.AsQueryable()
                .ProjectTo<FlagDto>(mapper, new MapParameters { ["minimumId"] = null }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ProjectTo_WhenTheMapReadsNoParameterButOneIsSupplied_ShouldThrow()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();
        var orders = new List<Order>();

        // Act & Assert
        await Assert.That(() => orders.AsQueryable()
                .ProjectTo<OrderSummaryDto>(mapper, new MapParameters { ["language"] = "en" }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetExpression_WhenBound_ShouldReadTheValueAsAFieldOnAConstant()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();

        // Act
        var expression = mapper.GetExpression<Product, ProductDto>(new MapParameters { ["language"] = "en" });

        // Assert
        var reads = new MemberReadCollector();
        reads.Visit(expression);
        await Assert.That(reads.Declarations).IsEqualTo(0).Because("no read of the declaration may survive binding");
        await Assert.That(reads.ValueFieldsOnConstants).IsEqualTo(1)
            .Because("EF Core parameterizes a member read on a constant, and inlines a bare constant");
    }

    [Test]
    public async Task Map_WhenTheMapReadsAParameter_ShouldThrowNamingIt()
    {
        // Arrange
        var mapper = MapperFactory.CreateFrom<ProductProfile>();

        // Act & Assert
        await Assert.That(() => mapper.Map<ProductDto>(Products[0]))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'language'");
    }

    [Test]
    public async Task AssertConfigurationIsValid_WhenAMemberReadsAParameter_ShouldCountItAsMapped()
    {
        // Arrange
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<ProductProfile>());

        // Act & Assert
        await Assert.That(configuration.AssertConfigurationIsValid).ThrowsNothing();
    }

    [Test]
    public async Task Parameter_WhenANameIsDeclaredWithTwoTypes_ShouldThrow()
    {
        // Act & Assert
        await Assert.That(() => new ConflictingProfile()).Throws<InvalidOperationException>();
    }

    // -------- Models and profiles --------

    public class Product
    {
        public int Id { get; set; }
        public List<ProductName> Names { get; set; } = [];
    }

    public class ProductName
    {
        public string Language { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class Order
    {
        public int Id { get; set; }
        public List<Product> Lines { get; set; } = [];
    }

    public class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class OrderDto
    {
        public int Id { get; set; }
        public List<ProductDto> Lines { get; set; } = [];
    }

    public class OrderSummaryDto
    {
        public int Id { get; set; }
    }

    public class FlagDto
    {
        public int Id { get; set; }
        public bool IsPreferred { get; set; }
    }

    public class ProductProfile : MapperProfile
    {
        public ProductProfile()
        {
            var language = Parameter<string>("language");

            CreateMap<Product, ProductDto>()
                .For(d => d.Name, s => s.Names
                    .OrderBy(n => n.Language == language.Value ? 0 : 1)
                    .Select(n => n.Text)
                    .FirstOrDefault() ?? "");
            CreateMap<Order, OrderDto>();
            CreateMap<Order, OrderSummaryDto>();
        }
    }

    public class FlagProfile : MapperProfile
    {
        public FlagProfile()
        {
            var preferredId = Parameter<int?>("preferredId");

            CreateMap<Product, FlagDto>()
                .For(d => d.IsPreferred, s => s.Id == preferredId.Value);
        }
    }

    public class ThresholdProfile : MapperProfile
    {
        public ThresholdProfile()
        {
            var minimumId = Parameter<int>("minimumId");

            CreateMap<Product, FlagDto>()
                .For(d => d.IsPreferred, s => s.Id >= minimumId.Value);
        }
    }

    public class ConflictingProfile : MapperProfile
    {
        public ConflictingProfile()
        {
            Parameter<string>("language");
            Parameter<int>("language");
        }
    }

    private sealed class MemberReadCollector : ExpressionVisitor
    {
        public int Declarations { get; private set; }
        public int ValueFieldsOnConstants { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.DeclaringType is { IsGenericType: true } type)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(MapParameter<>))
                    Declarations++;
                else if (definition.Name.StartsWith("MapParameterValue", StringComparison.Ordinal)
                         && node.Expression is ConstantExpression)
                    ValueFieldsOnConstants++;
            }

            return base.VisitMember(node);
        }
    }
}
