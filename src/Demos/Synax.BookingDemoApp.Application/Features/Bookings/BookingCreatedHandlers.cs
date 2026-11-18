using Microsoft.Extensions.Logging;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

/// <summary>
/// Outbox-delivered handler. Delivery is at-least-once, so this must be idempotent — sending
/// the same confirmation twice is acceptable for the demo; a real implementation would key on
/// <c>BookingId</c>.
/// </summary>
public class BookingConfirmationEmailHandler(ILogger<BookingConfirmationEmailHandler> logger)
    : INotificationHandler<BookingCreatedNotification>
{
    public Task HandleAsync(BookingCreatedNotification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "📧 Confirmation email to {Email}: booking {BookingId} for {PartySize} on {Date:g}.",
            notification.CustomerEmail, notification.BookingId, notification.PartySize, notification.Date);
        return Task.CompletedTask;
    }
}

/// <summary>Second handler for the same notification — fan-out comes free with IPublisher.</summary>
public class BookingAnalyticsHandler(ILogger<BookingAnalyticsHandler> logger)
    : INotificationHandler<BookingCreatedNotification>
{
    public Task HandleAsync(BookingCreatedNotification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "📊 Analytics: booking recorded for restaurant {RestaurantId}.", notification.RestaurantId);
        return Task.CompletedTask;
    }
}
