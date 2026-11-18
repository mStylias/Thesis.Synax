using Synax.BookingDemoApp.Application.Behaviors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

public class CancelBookingRequest : ICommand
{
    public Guid Id { get; set; }
}

// Mediator design limit: void commands (ICommand / IRequest<Unit>) cannot declare validation via
// IWithValidation<,> — there is no data-carrying response slot to stash the validated state in, so
// cancellation here is a plain tracked-fetch-and-mutate handler instead.
[SkipBehavior(typeof(RequestTimingBehavior<,>))]
public class CancelBookingHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<CancelBookingRequest>
{
    private readonly IRepository<Booking, BookingContext> _bookingRepo = unitOfWork.GetRepository<Booking>();

    public async Task HandleAsync(CancelBookingRequest request, CancellationToken cancellationToken)
    {
        var entity = await _bookingRepo.GetByIdAsync(request.Id, cancellationToken);

        // Not-found is a silent no-op — cancelling a booking that no longer exists (or was already
        // cancelled) has no observable effect either way.
        if (entity is null || entity.Status == BookingStatus.Cancelled)
        {
            return;
        }

        entity.Status = BookingStatus.Cancelled;
        _bookingRepo.Update(entity);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
