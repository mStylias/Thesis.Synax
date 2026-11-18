using Microsoft.EntityFrameworkCore;
using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

public class GetBookingStatsRequest : IQuery<Result<List<BookingStatusCount>, HttpError>>
{
    public Guid RestaurantId { get; set; }
}

/// <summary>The number of bookings in one status for a restaurant.</summary>
public record BookingStatusCount(string Status, int Count);

public class GetBookingStatsHandler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<GetBookingStatsRequest, Result<List<BookingStatusCount>, HttpError>>
{
    private readonly IRepository<Booking, BookingContext> _bookingRepo = unitOfWork.GetRepository<Booking>();

    public async Task<Result<List<BookingStatusCount>, HttpError>> HandleAsync(
        GetBookingStatsRequest request, CancellationToken cancellationToken)
    {
        // ExecuteAsync is the escape hatch for operators not on ISynaxQuery — here a GroupBy
        // aggregation. The grouping/count run in SQL; enum-to-string conversion happens in memory
        // afterwards (g.Key.ToString() inside the projection may not translate).
        var stats = await _bookingRepo
            .Query(q => q.Where(b => b.RestaurantId == request.RestaurantId))
            .ExecuteAsync(q => q
                .GroupBy(b => b.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken));

        return stats
            .Select(s => new BookingStatusCount(s.Key.ToString(), s.Count))
            .ToList();
    }
}
