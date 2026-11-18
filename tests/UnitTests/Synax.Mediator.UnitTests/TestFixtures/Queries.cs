namespace Synax.Mediator.UnitTests.TestFixtures;

public record BookingDto(Guid Id, string Name, decimal Price);

public record GetBookingQuery(Guid Id) : IQuery<BookingDto>
{
    public QueryOptions Options { get; set; } = new();

    public record QueryOptions
    {
        public BookingDto? PreloadedBooking { get; init; }
    }
}

public class GetBookingQueryHandler(HandlerSpy state)
    : IQueryHandler<GetBookingQuery, BookingDto>
{
    public Task<BookingDto> HandleAsync(GetBookingQuery query, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(GetBookingQueryHandler));

        if (query.Options is { PreloadedBooking: { } booking })
            return Task.FromResult(booking);

        return Task.FromResult(new BookingDto(query.Id, "Default Booking", 99.99m));
    }
}
