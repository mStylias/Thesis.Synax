using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Features.Reviews;

/// <summary>
/// Raised through the transactional outbox when a review is submitted. Its email handler
/// dead-letters permanently invalid recipients (see <c>ReviewThankYouEmailHandler</c>) — the row
/// stays in the outbox table as an inspectable dead letter. Stored name: <c>review-submitted</c>.
/// </summary>
public record ReviewSubmittedNotification : INotification
{
    public Guid ReviewId { get; init; }
    public Guid RestaurantId { get; init; }
    public int Rating { get; init; }
    public string ReviewerEmail { get; init; } = string.Empty;
}
