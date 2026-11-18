using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Bookings;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

public class GetBookingsRequest : IQuery<Result<KeysetPage<BookingResponse, Guid>, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public int PageSize { get; set; } = 20;
    public Guid? After { get; set; }
}

public class GetBookingsHandler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<GetBookingsRequest, Result<KeysetPage<BookingResponse, Guid>, HttpError>>
{
    private readonly IRepository<Booking, BookingContext> _bookingRepo = unitOfWork.GetRepository<Booking>();

    public async Task<Result<KeysetPage<BookingResponse, Guid>, HttpError>> HandleAsync(
        GetBookingsRequest request, CancellationToken cancellationToken)
    {
        // Keyset (cursor) pagination: stable under concurrent writes and fast on deep pages, unlike
        // offset paging. Pass the returned NextCursor back as `after` for the following page.
        return await _bookingRepo
            .QueryDto<BookingResponse>(q => q.Where(b => b.RestaurantId == request.RestaurantId))
            .ToKeysetPageAsync(b => b.Id, request.PageSize, after: request.After, cancellationToken: cancellationToken);
    }
}
