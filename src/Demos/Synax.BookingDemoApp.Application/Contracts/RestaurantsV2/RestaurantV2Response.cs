namespace Synax.BookingDemoApp.Application.Contracts.RestaurantsV2;

/// <summary>
/// The v2 restaurant list shape — built inline via <c>QuerySelect</c> (no mapper profile).
/// Init-property class (not a positional record) on purpose: the keyset page orders by
/// <c>Id</c> AFTER the projection, and EF can only reach into member-initializer projections —
/// constructor arguments are opaque to the query translator.
/// </summary>
public class RestaurantV2Response
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Cuisine { get; init; } = string.Empty;
    public int TableCount { get; init; }
}
