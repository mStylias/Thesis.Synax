using Microsoft.Extensions.Options;
using Synax.Identity;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Infrastructure.Seeding;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Worker;

/// <summary>
/// Periodically archives each tenant's old bookings by sending the same mediator request an
/// endpoint would — the application layer does not know or care that there is no HTTP request
/// behind this one.
/// </summary>
public sealed class BookingMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IUserContextScope userContext,
    IOptions<MaintenanceOptions> options,
    TimeProvider timeProvider,
    ILogger<BookingMaintenanceService> logger) : BackgroundService
{
    // The demo seeds two tenants; a real worker would page through them.
    private static readonly Guid[] TenantIds = [DemoData.TenantAId, DemoData.TenantBId];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.IntervalMinutes), timeProvider);

        do
        {
            try
            {
                await ArchiveAsync(settings, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed pass must not take the host down; the next tick retries.
                logger.LogError(exception, "Booking maintenance pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ArchiveAsync(MaintenanceOptions settings, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var cutoff = timeProvider.GetUtcNow().AddMonths(-settings.ArchiveBookingsOlderThanMonths);

        foreach (var tenantId in TenantIds)
        {
            // There is no principal here. BecomeOwner puts the ownership query filters and the
            // stamping interceptors into exactly the state a request from that tenant would.
            using var _ = userContext.BecomeOwner(tenantId);

            var result = await sender.SendAsync(
                new ArchiveOldBookingsRequest { OlderThan = cutoff }, cancellationToken);

            if (result.IsSuccess)
                logger.LogInformation("Tenant {TenantId}: archived {Count} bookings.", tenantId, result.Data);
            else
                logger.LogWarning("Tenant {TenantId}: archiving failed ({Error}).", tenantId, result.Error.ErrorCode);
        }

        await LogFleetTotalAsync(scope, cutoff, cancellationToken);
    }

    /// <summary>
    /// The counterpart to the loop above: archiving writes per tenant, but a fleet-wide read is
    /// one query under <c>BecomeAllOwners</c> — the ownership filter admits every owner's rows
    /// for the duration of the scope instead of the worker paging through tenants again.
    /// </summary>
    private async Task LogFleetTotalAsync(
        AsyncServiceScope scope, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        using var _ = userContext.BecomeAllOwners();

        var remaining = await unitOfWork.GetRepository<Booking>()
            .Query(q => q.Where(booking => booking.Date >= cutoff))
            .CountAsync(cancellationToken);

        logger.LogInformation("Across all tenants: {Count} bookings remain after the cutoff.", remaining);
    }
}

public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    public int IntervalMinutes { get; set; } = 5;

    public int ArchiveBookingsOlderThanMonths { get; set; } = 6;
}
