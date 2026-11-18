using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.OData.UnitTests;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Stock { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public CategoryDto? Category { get; set; }
    public List<TagDto> Tags { get; set; } = [];
}

public class CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Self-referencing, so <c>$levels</c> has a navigation it is legal on.</summary>
    public CategoryDto? Parent { get; set; }
}

public class TagDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>Has no conventional key — EDM model building requires <see cref="SynaxODataOptions.ConfigureModel"/>.</summary>
public class KeylessDto
{
    public string Code { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public static class TestData
{
    /// <summary>Stable ids so cursor/token assertions can pin literal values.</summary>
    public static Guid Id(int n) => new($"{n:x8}-0000-0000-0000-000000000000");

    public static List<ProductDto> Products()
    {
        // A three-deep category tree, so $levels has something to walk.
        var goods = new CategoryDto { Id = Id(12), Name = "Goods" };
        var hardware = new CategoryDto { Id = Id(13), Name = "Hardware", Parent = goods };
        var food = new CategoryDto { Id = Id(14), Name = "Food", Parent = goods };

        var electronics = new CategoryDto { Id = Id(10), Name = "Electronics", Parent = hardware };
        var groceries = new CategoryDto { Id = Id(11), Name = "Groceries", Parent = food };

        return
        [
            new ProductDto
            {
                Id = Id(1), Name = "Laptop", Stock = 3, Price = 1200m,
                Description = "Portable computer",
                CreatedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Category = electronics,
                Tags =
                [
                    new TagDto { Id = Id(20), Label = "sale" },
                    new TagDto { Id = Id(21), Label = "refurbished" },
                    new TagDto { Id = Id(22), Label = "clearance" }
                ]
            },
            new ProductDto
            {
                Id = Id(2), Name = "Phone", Stock = 15, Price = 800m,
                Description = "Pocket computer",
                CreatedAt = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
                Category = electronics
            },
            new ProductDto
            {
                Id = Id(3), Name = "Apples", Stock = 50, Price = 3.5m,
                Description = null,
                CreatedAt = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                Category = groceries,
                Tags = [new TagDto { Id = Id(23), Label = "organic" }]
            },
            new ProductDto
            {
                Id = Id(4), Name = "Bread", Stock = 8, Price = 2m,
                Description = "50% wholegrain",
                CreatedAt = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc),
                Category = groceries
            },
            new ProductDto
            {
                Id = Id(5), Name = "Monitor", Stock = 0, Price = 300m,
                Description = null,
                CreatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                Category = null
            }
        ];
    }

    public static ISynaxQuery<ProductDto> ProductQuery() => SynaxQuery.From(Products());

    /// <summary>
    /// The leaf categories, queried as the root type — the self-referencing <c>Parent</c> is what
    /// makes <c>$levels</c> legal, and it is only legal on a navigation back to the queried type.
    /// </summary>
    public static ISynaxQuery<CategoryDto> CategoryQuery()
        => SynaxQuery.From(Products().Select(product => product.Category).OfType<CategoryDto>()
            .DistinctBy(category => category.Id).ToList());

    /// <summary>Builds a GET request carrying the given query string (e.g. <c>"?$filter=..."</c>).</summary>
    public static HttpRequest CreateRequest(string queryString, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        services?.Invoke(collection);

        var context = new DefaultHttpContext { RequestServices = collection.BuildServiceProvider() };
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.PathBase = "/api";
        context.Request.Path = "/v1/products/search";
        context.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString(queryString);

        return context.Request;
    }

    /// <summary>
    /// Turns an <c>@odata.nextLink</c> back into a request, the way a client following the link would.
    /// </summary>
    public static HttpRequest FollowLink(string nextLink)
    {
        var separator = nextLink.IndexOf('?');
        return CreateRequest(separator < 0 ? string.Empty : nextLink[separator..]);
    }

    public static IReadOnlyList<string> Names(this ODataPage page)
        => page.Value.Cast<ProductDto>().Select(product => product.Name).ToList();

    public static IDictionary<string, object?> Row(this ODataPage page, int index)
        => (IDictionary<string, object?>)page.Value[index]!;

    /// <summary>The normalized dictionary a <c>$select</c>/<c>$expand</c> row nests under a member.</summary>
    public static IDictionary<string, object?> Nested(this IDictionary<string, object?> row, string member)
        => (IDictionary<string, object?>)row[member]!;

    /// <summary>The normalized list a <c>$expand</c> of a collection navigation nests under a member.</summary>
    public static List<object?> NestedList(this IDictionary<string, object?> row, string member)
        => (List<object?>)row[member]!;
}
