using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

public class ArchiveOldBookingsRequest : ICommand<Result<int, HttpError>>
{
    public DateTimeOffset OlderThan { get; set; }
}

public class ArchiveOldBookingsHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<ArchiveOldBookingsRequest, Result<int, HttpError>>
{
    private readonly IRepository<Booking, BookingContext> _bookingRepo = unitOfWork.GetRepository<Booking>();

    public async Task<Result<int, HttpError>> HandleAsync(
        ArchiveOldBookingsRequest request, CancellationToken cancellationToken)
    {
        // Set-based ExecuteUpdateAsync: a single UPDATE statement that bypasses the change tracker
        // and acts immediately — no SaveChangesAsync involved.
        var count = await _bookingRepo.ExecuteUpdateAsync(
            q => q.Where(b => b.Date < request.OlderThan &&
                              (b.Status == BookingStatus.Completed || b.Status == BookingStatus.Cancelled)),
            set => set.SetProperty(b => b.Status, BookingStatus.Archived),
            cancellationToken);

        return Result.Ok<int, HttpError>(count).AddMessage($"Archived {count} bookings.");
    }
}
