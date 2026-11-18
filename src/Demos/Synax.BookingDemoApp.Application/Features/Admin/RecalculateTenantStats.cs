using Synax.AspNetCore.Errors;
using Synax.AspNetCore.Identity;
using Synax.Identity;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Admin;

public class RecalculateTenantStatsRequest : ICommand<Result<TenantStatsResponse, HttpError>>
{
    public Guid TenantId { get; set; }
}

public record TenantStatsResponse(Guid TenantId, int Restaurants, int Tables, int Bookings, int MenuItems);

public class RecalculateTenantStatsHandler(
    ICurrentUser currentUser,
    IUserContextScope userContext,
    IUnitOfWork<BookingContext> unitOfWork,
    IPublisher publisher)
    : ICommandHandler<RecalculateTenantStatsRequest, Result<TenantStatsResponse, HttpError>>
{
    public async Task<Result<TenantStatsResponse, HttpError>> HandleAsync(
        RecalculateTenantStatsRequest request, CancellationToken cancellationToken)
    {
        // Role gate via the raw principal — the escape hatch for claims that ICurrentUser
        // deliberately does not model (roles, tenant, email, ...).
        if (currentUser.Principal?.FindFirst("role")?.Value != nameof(UserRole.Owner))
        {
            return HttpError.Forbidden(
                "Only owners may recalculate stats.", errorCode: ErrorCodes.Common.Forbidden);
        }

        int restaurants, tables, bookings, menuItems;

        // Cross-tenant admin op: inside this scope every ownership-filtered query sees the
        // TARGET tenant's rows, regardless of the caller's own tenant claim. The counts must
        // be captured inside the using block — outside it the caller's tenant applies again.
        using (userContext.BecomeOwner(request.TenantId))
        {
            restaurants = await unitOfWork.GetRepository<Restaurant>().CountAsync(cancellationToken);
            tables = await unitOfWork.GetRepository<Table>().CountAsync(cancellationToken);
            bookings = await unitOfWork.GetRepository<Booking>().CountAsync(cancellationToken);
            menuItems = await unitOfWork.GetRepository<MenuItem>().CountAsync(cancellationToken);
        }

        // Per-call strategy override: parallel in-process fan-out. This is a transient
        // notification — if the process dies, it is simply lost, which costs nothing here.
        // Contrast with the durable outbox used for business events (BookingCreated), where
        // delivery must survive crashes and rollbacks.
        await publisher.PublishAsync(
            new TenantStatsRecalculatedNotification(request.TenantId, restaurants, bookings),
            NotificationPublishStrategy.Parallel,
            cancellationToken);

        return new TenantStatsResponse(request.TenantId, restaurants, tables, bookings, menuItems);
    }
}
