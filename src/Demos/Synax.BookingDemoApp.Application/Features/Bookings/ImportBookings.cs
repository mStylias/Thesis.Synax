using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

public class ImportBookingsRequest : ICommand<Result<int, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public List<ImportBookingItem> Items { get; set; } = [];
}

public class ImportBookingItem
{
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public int PartySize { get; set; }
    public BookingStatus Status { get; set; }

    /// <summary>The historical creation time carried over from the source system.</summary>
    public DateTime CreatedAt { get; set; }
}

public class ImportBookingsHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<ImportBookingsRequest, Result<int, HttpError>>
{
    private readonly IRepository<Booking, BookingContext> _bookingRepo = unitOfWork.GetRepository<Booking>();

    public async Task<Result<int, HttpError>> HandleAsync(
        ImportBookingsRequest request, CancellationToken cancellationToken)
    {
        // Id stays at default: bulk insert runs the client-side Guid value generator.
        // CreatedAt is set explicitly to the historical import time — the auditing stamper only
        // stamps defaults, so an explicit non-default value wins and the backdate survives.
        // OwnerId is NOT set: bulk insert stamps the ambient owner, same as a normal save.
        var entities = request.Items.Select(item => new Booking
        {
            RestaurantId = request.RestaurantId,
            TableId = request.TableId,
            CustomerName = item.CustomerName,
            CustomerEmail = item.CustomerEmail,
            Date = item.Date,
            PartySize = item.PartySize,
            Status = item.Status,
            CreatedAt = item.CreatedAt
        }).ToList();

        return await Result.TryAsync(
            async () =>
            {
                // Provider-optimized bulk insert (COPY on PostgreSQL) — acts immediately,
                // no SaveChangesAsync involved.
                await _bookingRepo.BulkInsertAsync(entities, cancellationToken);
                return entities.Count;
            },
            ex => HttpError.Internal("Bulk import failed.", errorCode: ErrorCodes.Bookings.ImportFailed));
    }
}
