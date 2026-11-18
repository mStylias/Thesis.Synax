namespace Synax.BookingDemoApp.Application.Contracts.Reviews;

/// <summary>
/// The review wire shape. Used both in-memory (<c>Map</c>) and as a raw EF projection
/// (<c>GetExpression</c> + <c>QuerySelect</c>), so members keep SQL-translatable source types.
/// </summary>
public class ReviewResponse
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string ReviewerEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
