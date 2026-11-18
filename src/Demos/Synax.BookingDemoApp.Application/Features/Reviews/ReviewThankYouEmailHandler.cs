using Microsoft.Extensions.Logging;
using Synax.EntityFrameworkCore.Mediator;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Features.Reviews;

/// <summary>
/// Outbox-delivered handler — the dead-letter showcase. A permanently invalid recipient can
/// never succeed, so retrying is pointless: throwing <see cref="OutboxDeadLetterException"/>
/// skips all retries and the outbox row stays in the table as an inspectable dead letter.
/// Delivery is at-least-once, so the happy path must be idempotent — sending the same
/// thank-you email twice is acceptable by nature here.
/// </summary>
public class ReviewThankYouEmailHandler(ILogger<ReviewThankYouEmailHandler> logger)
    : INotificationHandler<ReviewSubmittedNotification>
{
    public Task HandleAsync(ReviewSubmittedNotification notification, CancellationToken cancellationToken)
    {
        if (!notification.ReviewerEmail.Contains('@') ||
            notification.ReviewerEmail.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        {
            // No retries — the row remains in the outbox table as a dead letter for inspection.
            throw new OutboxDeadLetterException(
                $"Recipient '{notification.ReviewerEmail}' is permanently invalid.");
        }

        logger.LogInformation(
            "📧 Thank you email to {Email}: review {ReviewId} ({Rating}/5) for restaurant {RestaurantId}.",
            notification.ReviewerEmail, notification.ReviewId, notification.Rating, notification.RestaurantId);
        return Task.CompletedTask;
    }
}
