namespace Synax.BookingDemoApp.Application.Contracts.Tables;

/// <summary>Also used as the element type of <c>RestaurantDetailResponse.Tables</c> (nested/collection mapping).</summary>
public class TableResponse
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public int Number { get; set; }
    public int Seats { get; set; }
    public string Location { get; set; } = string.Empty;
}
