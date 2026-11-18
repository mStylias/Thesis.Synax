using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.UnitTests.TestSupport;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: the fake's audit-stamping emulation — it runs the library's own
/// <c>AuditStamper</c>, so creation stamps (<c>CreatedAt</c>/<c>CreatedById</c>) on add and
/// modification stamps (<c>UpdatedAt</c>/<c>ModifiedById</c>) on update behave exactly like the
/// real auditing interceptor, with a deterministic pinned clock and ambient user.
/// </summary>
public class AuditStampingTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 6, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static Booking NewBooking() => new()
    {
        Id = Guid.NewGuid(),
        RestaurantId = Guid.NewGuid(),
        TableId = Guid.NewGuid(),
        CustomerName = "Grace Hopper",
        CustomerEmail = "grace@example.com",
        Date = FixedNow.AddDays(1),
        PartySize = 2,
        Status = BookingStatus.Confirmed
    };

    [Test]
    public async Task AddAndSave_StampsCreatedAtAndCreatedById()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant, user: User, clock: new FixedTimeProvider(FixedNow));
        var booking = NewBooking();

        await uow.GetRepository<Booking>().AddAsync(booking);
        await uow.SaveChangesAsync();

        await Assert.That(booking.CreatedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(booking.CreatedById).IsEqualTo(User);
        await Assert.That(booking.UpdatedAt).IsNull();
        await Assert.That(booking.ModifiedById).IsNull();
    }

    [Test]
    public async Task UpdateAndSave_StampsUpdatedAtAndModifiedById_LeavingCreationUntouched()
    {
        var created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var creator = Guid.NewGuid();
        var booking = NewBooking();
        booking.CreatedAt = created;
        booking.CreatedById = creator;
        booking.OwnerId = Tenant;

        var uow = TestWorld.CreateUow(tenant: Tenant, user: User, clock: new FixedTimeProvider(FixedNow));
        uow.Seed(booking);

        booking.PartySize = 6;
        uow.GetRepository<Booking>().Update(booking);
        await uow.SaveChangesAsync();

        await Assert.That(booking.UpdatedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(booking.ModifiedById).IsEqualTo(User);
        // The creation trail is preserved on update — same as the real interceptor.
        await Assert.That(booking.CreatedAt).IsEqualTo(created);
        await Assert.That(booking.CreatedById).IsEqualTo(creator);
    }
}
