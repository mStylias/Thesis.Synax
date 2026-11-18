namespace Synax.BookingDemoApp.Application.Contracts.Bookings;

/// <summary>
/// The booking wire shape. <c>Status</c> is a string — the mapper's enum→string strategy
/// converts it automatically. Used both in-memory (<c>Map</c>) and as an EF projection
/// (<c>QueryDto</c>), so members keep SQL-translatable source types.
/// </summary>
public class BookingResponse
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public int PartySize { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
