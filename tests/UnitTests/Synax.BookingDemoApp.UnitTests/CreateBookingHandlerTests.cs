using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.UnitTests.TestSupport;
using Synax.EntityFrameworkCore.Testing;
using Synax.Mediator;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: direct handler invocation with <c>ValidateAndHandleAsync</c> (data-carrying
/// validation via <c>IWithValidation</c> + <c>this.ValidationData()</c>), the
/// <c>FakeUnitOfWork</c> from Synax.EntityFrameworkCore.Testing, a recording outbox fake, and a
/// pinned <see cref="TimeProvider"/> — the flagship "no database, no DI container" handler test.
/// </summary>
public class CreateBookingHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static (CreateBookingHandler Handler, FakeUnitOfWork<BookingContext> Uow, FakeOutbox Outbox, Table Table)
        CreateWorld()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant, user: User, clock: new FixedTimeProvider(FixedNow));
        var table = new Table
        {
            Id = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Number = 1,
            Seats = 4,
            Location = TableLocation.Indoor,
            OwnerId = Tenant
        };
        uow.Seed(table);

        var outbox = new FakeOutbox();
        var handler = new CreateBookingHandler(uow, TestWorld.Mapper, outbox, new FixedTimeProvider(FixedNow));
        return (handler, uow, outbox, table);
    }

    private static CreateBookingRequest ValidRequest(Table table) => new()
    {
        RestaurantId = table.RestaurantId,
        TableId = table.Id,
        CustomerName = "Ada Lovelace",
        CustomerEmail = "ada@example.com",
        Date = FixedNow.AddDays(3),
        PartySize = 3,
        Notes = "window seat"
    };

    [Test]
    public async Task UnknownTable_FailsWithTableNotFound_AndHandlerNeverRuns()
    {
        var (handler, uow, outbox, table) = CreateWorld();
        var request = ValidRequest(table);
        request.TableId = Guid.NewGuid();   // not seeded

        var result = await handler.ValidateAndHandleAsync(request);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo(ErrorCodes.Bookings.TableNotFound);
        await Assert.That(result.Error!.HttpCode).IsEqualTo(404);
        // Validation short-circuited before HandleAsync: nothing staged, nothing enqueued.
        await Assert.That(uow.Entities<Booking>()).IsEmpty();
        await Assert.That(outbox.Enqueued).IsEmpty();
    }

    [Test]
    public async Task PartyLargerThanTable_FailsWith400_AndFieldLevelValidationError()
    {
        var (handler, _, _, table) = CreateWorld();
        var request = ValidRequest(table);
        request.PartySize = 10;   // table seats 4

        var result = await handler.ValidateAndHandleAsync(request);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.HttpCode).IsEqualTo(400);
        await Assert.That(result.Error!.ErrorCode).IsEqualTo("VALIDATION_FAILED");
        var fieldError = result.Error!.ValidationErrors!.Single();
        await Assert.That(fieldError.Code).IsEqualTo(ErrorCodes.Bookings.PartyTooLarge);
        await Assert.That(fieldError.PropertyName).IsEqualTo("PartySize");
    }

    [Test]
    public async Task DateNotInFuture_FailsValidation_UsingThePinnedClock()
    {
        var (handler, _, _, table) = CreateWorld();
        var request = ValidRequest(table);
        request.Date = FixedNow.AddMinutes(-1);   // in the past relative to the fixed clock

        var result = await handler.ValidateAndHandleAsync(request);

        await Assert.That(result.IsError).IsTrue();
        var fieldError = result.Error!.ValidationErrors!.Single();
        await Assert.That(fieldError.Code).IsEqualTo(ErrorCodes.Bookings.DateInPast);
        await Assert.That(fieldError.PropertyName).IsEqualTo("Date");
    }

    [Test]
    public async Task PartyTooLargeAndDateInPast_ReportTogether_InOneValidationResponse()
    {
        var (handler, _, _, table) = CreateWorld();
        var request = ValidRequest(table);
        request.PartySize = 10;                   // table seats 4
        request.Date = FixedNow.AddMinutes(-1);   // in the past relative to the fixed clock

        var result = await handler.ValidateAndHandleAsync(request);

        // Field rules accumulate: one 400 carries every invalid field at once.
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.HttpCode).IsEqualTo(400);
        var codes = result.Error!.ValidationErrors!.Select(e => e.Code).ToList();
        await Assert.That(codes).Contains(ErrorCodes.Bookings.PartyTooLarge);
        await Assert.That(codes).Contains(ErrorCodes.Bookings.DateInPast);
    }

    [Test]
    public async Task HappyPath_PersistsConfirmedBooking_EnqueuesOutboxNotification_AndMapsResponse()
    {
        var (handler, uow, outbox, table) = CreateWorld();

        var result = await handler.ValidateAndHandleAsync(ValidRequest(table));

        await Assert.That(result.IsSuccess).IsTrue();

        var booking = uow.Entities<Booking>().Single();
        await Assert.That(booking.Status).IsEqualTo(BookingStatus.Confirmed);
        await Assert.That(booking.CustomerName).IsEqualTo("Ada Lovelace");
        // Stamped by the emulated ownership interceptor on save — the handler never set it.
        await Assert.That(booking.OwnerId).IsEqualTo(Tenant);

        var notification = (BookingCreatedNotification)outbox.Enqueued.Single();
        await Assert.That(notification.BookingId).IsEqualTo(booking.Id);
        await Assert.That(notification.PartySize).IsEqualTo(3);

        // Enum→string conversion happened in the entity→response map.
        await Assert.That(result.Data!.Status).IsEqualTo("Confirmed");
        await Assert.That(result.Data!.Id).IsEqualTo(booking.Id);
    }

    [Test]
    public async Task HandlerConsumesTheTableFetchedDuringValidation()
    {
        var (handler, uow, _, table) = CreateWorld();

        var result = await handler.ValidateAndHandleAsync(ValidRequest(table));

        // HandleAsync read the table via this.ValidationData() and stamped its id on the booking.
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(uow.Entities<Booking>().Single().TableId).IsEqualTo(table.Id);
    }
}
