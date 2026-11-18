using Synax.BookingDemoApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves the provider-optimized bulk insert (Npgsql binary COPY) end to end through the
/// ImportBookings handler: client-side Guid key generation, creation-audit stamping
/// (explicit backdated CreatedAt wins over the stamper; CreatedById from the ambient user),
/// and ownership stamping from the ambient tenant — all on rows written outside the change
/// tracker, which the in-memory fakes cannot exercise.
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class BulkInsertTests(BookingFixture fixture)
{
    [Test]
    public async Task ImportBookings_ShouldBulkInsertWithGeneratedIds_BackdatedCreatedAt_AndAmbientStamps()
    {
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();

        // The user must exist before audited rows reference it (CreatedById is a real FK).
        fixture.ActAsNobody();
        await fixture.SeedAsync(
            TestData.User(userId, tenant),
            TestData.Cuisine(cuisineId, ownerId: tenant),
            TestData.Restaurant(restaurantId, tenant, cuisineId),
            TestData.Table(tableId, restaurantId, tenant));

        fixture.ActAs(userId, tenant);

        var backdate = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var request = new ImportBookingsRequest
        {
            RestaurantId = restaurantId,
            TableId = tableId,
            Items =
            [
                new ImportBookingItem
                {
                    CustomerName = "Import One",
                    CustomerEmail = "one@import.demo",
                    Date = new DateTimeOffset(2020, 5, 10, 19, 0, 0, TimeSpan.Zero),
                    PartySize = 2,
                    Status = BookingStatus.Completed,
                    CreatedAt = backdate
                },
                new ImportBookingItem
                {
                    CustomerName = "Import Two",
                    CustomerEmail = "two@import.demo",
                    Date = new DateTimeOffset(2020, 6, 10, 19, 0, 0, TimeSpan.Zero),
                    PartySize = 4,
                    Status = BookingStatus.Cancelled,
                    CreatedAt = backdate.AddDays(1)
                },
                new ImportBookingItem
                {
                    CustomerName = "Import Three",
                    CustomerEmail = "three@import.demo",
                    Date = new DateTimeOffset(2020, 7, 10, 19, 0, 0, TimeSpan.Zero),
                    PartySize = 6,
                    Status = BookingStatus.Completed,
                    CreatedAt = backdate.AddDays(2)
                }
            ]
        };

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(request);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo(3);

        var imported = await GetImportedBookingsAsync(tableId);
        await Assert.That(imported).Count().IsEqualTo(3);

        // Ids were generated client-side by the bulk-insert value generator: non-empty, distinct.
        await Assert.That(imported.All(b => b.Id != Guid.Empty)).IsTrue();
        await Assert.That(imported.Select(b => b.Id).Distinct().Count()).IsEqualTo(3);

        // Explicit-value-wins: the historical CreatedAt survived the creation stamper.
        var byName = imported.ToDictionary(b => b.CustomerName);
        await Assert.That(byName["Import One"].CreatedAt).IsEqualTo(backdate);
        await Assert.That(byName["Import Two"].CreatedAt).IsEqualTo(backdate.AddDays(1));
        await Assert.That(byName["Import Three"].CreatedAt).IsEqualTo(backdate.AddDays(2));

        // Ambient stamps: OwnerId from the tenant, CreatedById from the acting user.
        await Assert.That(imported.All(b => b.OwnerId == tenant)).IsTrue();
        await Assert.That(imported.All(b => b.CreatedById == userId)).IsTrue();
    }

    private async Task<List<Booking>> GetImportedBookingsAsync(Guid tableId)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        return await unitOfWork.GetRepository<Booking>()
            .Query(q => q.Where(b => b.TableId == tableId))
            .ToListAsync();
    }
}
