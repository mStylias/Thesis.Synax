using Synax.BookingDemoApp.Application.Contracts.Tables;

namespace Synax.BookingDemoApp.Application.Contracts.Restaurants;

/// <summary>
/// The restaurant list/summary wire shape. Used both in-memory (<c>Map</c>) and as an EF
/// projection (<c>QueryDto</c>), so members keep SQL-translatable source types.
/// </summary>
public class RestaurantResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid CuisineId { get; set; }
    public int MaxCapacity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// The restaurant detail wire shape — adds the cuisine name and the nested tables collection
/// (bound via the already-registered <c>Table</c> → <c>TableResponse</c> element map).
/// </summary>
public class RestaurantDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid CuisineId { get; set; }
    public int MaxCapacity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CuisineName { get; set; } = string.Empty;
    public List<TableResponse> Tables { get; set; } = [];
}

/// <summary>
/// The same summary as a <b>positional record</b>, with a table count aggregated over the
/// navigation.
/// </summary>
/// <remarks>
/// Shape, not convenience. A record is projected through its constructor rather than an object
/// initializer, and a client option composed over that projection produces
/// <c>new RestaurantRecordResponse(...).Name</c>. EF Core can only reduce that back to the
/// argument when the projection names the member each argument initializes, so this type is what
/// keeps <c>$filter</c>, <c>$orderby</c>, <c>$top</c> and <c>$skip</c> honest for every record
/// DTO. <c>TableCount</c> is a subquery, which is what makes an unreduced constructor fatal
/// rather than merely verbose: no provider can put one inside an ORDER BY.
/// </remarks>
public record RestaurantRecordResponse(Guid Id, string Name, int MaxCapacity, int TableCount);
