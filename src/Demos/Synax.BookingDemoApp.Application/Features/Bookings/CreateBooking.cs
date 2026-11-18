using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Application.Behaviors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Bookings;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Bookings;

public class CreateBookingRequest : ICommand<Result<BookingResponse, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public int PartySize { get; set; }
    public string? Notes { get; set; }
}

[MediatorBehavior(typeof(BookingTransactionBehavior<,>))]
public class CreateBookingHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper,
    IOutbox<BookingContext> outbox,
    TimeProvider timeProvider)
    : ICommandHandler<CreateBookingRequest, Result<BookingResponse, HttpError>>,
      IWithValidation<CreateBookingRequest, Result<Table, HttpError>>
{
    private readonly IRepository<Booking, BookingContext> _bookingRepo = unitOfWork.GetRepository<Booking>();
    private readonly IRepository<Table, BookingContext> _tableRepo = unitOfWork.GetRepository<Table>();

    /// <summary>
    /// Data-carrying validation ("parse, don't validate"): the table is fetched once — a missing
    /// table returns 404 alone — then the field rules reuse the fetched data and accumulate
    /// through the <see cref="Validation"/> context into a single 400.
    /// <see cref="HandleAsync"/> reads the table back via <c>this.ValidationData()</c>,
    /// typed and non-null.
    /// </summary>
    public async Task<Result<Table, HttpError>> ValidateAsync(
        CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var table = await _tableRepo.Query(
                q => q.Where(t => t.Id == request.TableId && t.RestaurantId == request.RestaurantId))
            .FirstOrDefaultAsync(cancellationToken);
        if (table is null)
        {
            return HttpError.NotFound(
                "The specified table was not found in this restaurant.",
                errorCode: ErrorCodes.Bookings.TableNotFound);
        }

        return new Validation("The booking request is invalid.")
            .Require(request.PartySize <= table.Seats,
                ErrorCodes.Bookings.PartyTooLarge, $"Must be at most {table.Seats}.", nameof(request.PartySize))
            .Require(request.Date > timeProvider.GetUtcNow(),
                ErrorCodes.Bookings.DateInPast, "Must be a future date.", nameof(request.Date))
            .ToResult(table);
    }

    public async Task<Result<BookingResponse, HttpError>> HandleAsync(
        CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var table = this.ValidationData();   // validated + fetched once in ValidateAsync

        return await SaveBookingAsync(request, table, cancellationToken)
            .MapAsync(entity => mapper.Map<BookingResponse>(entity));
    }

    private async Task<Result<Booking, HttpError>> SaveBookingAsync(
        CreateBookingRequest request, Table table, CancellationToken cancellationToken)
    {
        // Commit/rollback is handled by BookingTransactionBehavior (see [MediatorBehavior]):
        // it commits when this returns a successful Result and rolls back on a failed one.
        // The outbox row is saved in the same SaveChanges as the booking — atomically. The
        // notification is only dispatched after the transaction commits; on rollback it never
        // existed. OwnerId/CreatedAt/CreatedById are stamped by the interceptors.
        return await Result.TryAsync(
            async () =>
            {
                var entity = mapper.Map<Booking>(request);
                entity.TableId = table.Id;

                await _bookingRepo.AddAsync(entity, cancellationToken);

                await outbox.EnqueueAsync(new BookingCreatedNotification
                {
                    BookingId = entity.Id,
                    RestaurantId = entity.RestaurantId,
                    CustomerName = entity.CustomerName,
                    CustomerEmail = entity.CustomerEmail,
                    Date = entity.Date,
                    PartySize = entity.PartySize
                }, cancellationToken);

                await unitOfWork.SaveChangesAsync(cancellationToken);
                return entity;
            },
            ex => HttpError.Internal("Failed to create booking.", errorCode: ErrorCodes.Bookings.CreateFailed));
    }
}
