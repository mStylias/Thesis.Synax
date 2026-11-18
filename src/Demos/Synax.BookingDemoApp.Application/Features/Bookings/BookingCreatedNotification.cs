using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

/// <summary>
/// Raised through the transactional outbox when a booking is created — stored atomically with
/// the booking row and published by the outbox dispatcher after commit (at-least-once, so
/// handlers must be idempotent). Stored under the stable name <c>booking-created</c>.
/// </summary>
public record BookingCreatedNotification : INotification
{
    public Guid BookingId { get; init; }
    public Guid RestaurantId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public DateTimeOffset Date { get; init; }
    public int PartySize { get; init; }
}
