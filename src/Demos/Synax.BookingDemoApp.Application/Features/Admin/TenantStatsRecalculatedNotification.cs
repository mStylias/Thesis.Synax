using Microsoft.Extensions.Logging;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Features.Admin;

/// <summary>
/// Transient in-process notification published directly via <c>IPublisher</c> (with the
/// Parallel strategy) — not through the outbox, because losing one on a crash is harmless.
/// </summary>
public record TenantStatsRecalculatedNotification(Guid TenantId, int Restaurants, int Bookings)
    : INotification;

public class TenantStatsRecalculatedLogHandler(ILogger<TenantStatsRecalculatedLogHandler> logger)
    : INotificationHandler<TenantStatsRecalculatedNotification>
{
    public Task HandleAsync(TenantStatsRecalculatedNotification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Tenant {TenantId} stats recalculated: {Restaurants} restaurants, {Bookings} bookings.",
            notification.TenantId, notification.Restaurants, notification.Bookings);
        return Task.CompletedTask;
    }
}
