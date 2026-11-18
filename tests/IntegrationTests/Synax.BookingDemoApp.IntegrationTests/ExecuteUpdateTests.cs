using Synax.BookingDemoApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Application.Features.MenuItems;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves set-based <c>ExecuteUpdateAsync</c> against real SQL — the fakes' known limitation
/// (EF's setters builder is not interpretable in-memory, so <c>FakeUnitOfWork</c> throws).
/// Covers the demo's two flavors: a constant setter (ArchiveOldBookings) and a computed setter
/// with arithmetic + rounding translated to SQL (AdjustPrices), both dispatched through the
/// real mediator pipeline (including <c>IWithValidation</c>).
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class ExecuteUpdateTests(BookingFixture fixture)
{
    [Test]
    public async Task ArchiveOldBookings_ShouldArchiveOnlyOldCompletedOrCancelled_ViaSingleUpdateStatement()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var oldCompletedId = Guid.NewGuid();
        var oldCancelledId = Guid.NewGuid();
        var futureCompletedId = Guid.NewGuid();
        var oldConfirmedId = Guid.NewGuid();

        fixture.ActAsTenant(tenant);
        var oldDate = DateTimeOffset.UtcNow.AddDays(-30);
        var futureDate = DateTimeOffset.UtcNow.AddDays(10);
        await fixture.SeedAsync(
            TestData.Cuisine(cuisineId, ownerId: tenant),
            TestData.Restaurant(restaurantId, tenant, cuisineId),
            TestData.Table(tableId, restaurantId, tenant),
            TestData.Booking(oldCompletedId, restaurantId, tableId, tenant, oldDate, BookingStatus.Completed),
            TestData.Booking(oldCancelledId, restaurantId, tableId, tenant, oldDate, BookingStatus.Cancelled),
            TestData.Booking(futureCompletedId, restaurantId, tableId, tenant, futureDate, BookingStatus.Completed),
            TestData.Booking(oldConfirmedId, restaurantId, tableId, tenant, oldDate, BookingStatus.Confirmed));

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(
            new ArchiveOldBookingsRequest { OlderThan = DateTimeOffset.UtcNow });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo(2);

        // Fresh query: the UPDATE bypassed the change tracker, so this is real database state.
        var statusById = await GetBookingStatusesAsync(
            oldCompletedId, oldCancelledId, futureCompletedId, oldConfirmedId);
        await Assert.That(statusById[oldCompletedId]).IsEqualTo(BookingStatus.Archived);
        await Assert.That(statusById[oldCancelledId]).IsEqualTo(BookingStatus.Archived);
        await Assert.That(statusById[futureCompletedId]).IsEqualTo(BookingStatus.Completed);
        await Assert.That(statusById[oldConfirmedId]).IsEqualTo(BookingStatus.Confirmed);
    }

    [Test]
    public async Task AdjustPrices_ShouldMultiplyAndRoundInSql_WithComputedSetter()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var roundItemId = Guid.NewGuid();
        var fractionalItemId = Guid.NewGuid();

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            TestData.Cuisine(cuisineId, ownerId: tenant),
            TestData.Restaurant(restaurantId, tenant, cuisineId),
            TestData.MenuItem(roundItemId, restaurantId, tenant, price: 10.00m),
            TestData.MenuItem(fractionalItemId, restaurantId, tenant, price: 3.33m));

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(
            new AdjustPricesRequest { RestaurantId = restaurantId, Percent = 10m });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo(2);

        var priceById = await GetMenuPricesAsync(roundItemId, fractionalItemId);
        await Assert.That(priceById[roundItemId]).IsEqualTo(11.00m);
        // 3.33 * 1.10 = 3.663 — rounded to 3.66 by the SQL-translated Math.Round(..., 2).
        await Assert.That(priceById[fractionalItemId]).IsEqualTo(3.66m);
    }

    private async Task<Dictionary<Guid, BookingStatus>> GetBookingStatusesAsync(params Guid[] ids)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        var bookings = await unitOfWork.GetRepository<Booking>()
            .Query(q => q.Where(b => ids.Contains(b.Id)))
            .ToListAsync();

        return bookings.ToDictionary(b => b.Id, b => b.Status);
    }

    private async Task<Dictionary<Guid, decimal>> GetMenuPricesAsync(params Guid[] ids)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        var items = await unitOfWork.GetRepository<MenuItem>()
            .Query(q => q.Where(m => ids.Contains(m.Id)))
            .ToListAsync();

        return items.ToDictionary(m => m.Id, m => m.Price);
    }
}
