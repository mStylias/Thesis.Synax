using Synax.BookingDemoApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Application.Features.Reviews;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator;
using Synax.Mediator;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves the transactional outbox against real PostgreSQL and the real dispatcher hosted
/// service: happy-path dispatch deletes the row after the wrapping transaction commits
/// (CreateBooking, via <c>BookingTransactionBehavior</c>); a handler throwing
/// <c>OutboxDeadLetterException</c> leaves an inspectable dead-letter row that survives sweep
/// cycles (CreateReview to a permanently invalid recipient); and a rolled-back transaction
/// leaves no row and dispatches nothing.
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class OutboxEndToEndTests(BookingFixture fixture)
{
    [Test]
    public async Task CreateBooking_OnCommit_ShouldDispatchNotificationAndDeleteOutboxRow()
    {
        await fixture.StartDispatcherAsync();

        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(
            TestData.User(userId, tenant),
            TestData.Cuisine(cuisineId, ownerId: tenant),
            TestData.Restaurant(restaurantId, tenant, cuisineId),
            TestData.Table(tableId, restaurantId, tenant, seats: 6));

        fixture.ActAs(userId, tenant);

        Guid bookingId;
        await using (var scope = fixture.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.SendAsync(new CreateBookingRequest
            {
                RestaurantId = restaurantId,
                TableId = tableId,
                CustomerName = "Outbox Customer",
                CustomerEmail = "outbox@customers.demo",
                Date = DateTimeOffset.UtcNow.AddDays(7),
                PartySize = 2
            });

            await Assert.That(result.IsSuccess).IsTrue();
            bookingId = result.Data!.Id;
        }

        // The booking row committed with the outbox row; post-commit the dispatcher publishes
        // the notification and deletes the row — the empty(-for-this-marker) table is the proof.
        var marker = bookingId.ToString();
        var dispatched = await BookingFixture.PollUntilAsync(async () =>
            (await fixture.GetOutboxRowsAsync()).All(r => !r.Payload.Contains(marker)));

        await Assert.That(dispatched).IsTrue();
        await Assert.That(await BookingExistsAsync(bookingId)).IsTrue();
    }

    [Test]
    public async Task CreateReview_WithPermanentlyInvalidRecipient_ShouldDeadLetterAndNeverDelete()
    {
        await fixture.StartDispatcherAsync();

        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            TestData.Cuisine(cuisineId, ownerId: tenant),
            TestData.Restaurant(restaurantId, tenant, cuisineId));

        Guid reviewId;
        await using (var scope = fixture.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.SendAsync(new CreateReviewRequest
            {
                RestaurantId = restaurantId,
                Rating = 5,
                Comment = "Great, but unreachable.",
                // No '@' and a .invalid suffix — ReviewThankYouEmailHandler throws
                // OutboxDeadLetterException: retrying can never succeed.
                ReviewerEmail = "nobody.invalid"
            });

            await Assert.That(result.IsSuccess).IsTrue();
            reviewId = result.Data!.Id;
        }

        var marker = reviewId.ToString();
        var deadLettered = await BookingFixture.PollUntilAsync(async () =>
            (await fixture.GetOutboxRowsAsync()).Any(r =>
                r.Payload.Contains(marker)
                && r.Attempts >= BookingFixture.OutboxMaxAttempts
                && r.Error is not null
                && r.Error.Contains("permanently invalid")));

        await Assert.That(deadLettered).IsTrue();

        // Several 1s sweep cycles: a not-quite-dead row would be retried and (on another dead
        // letter) mutated, a dispatched one deleted. The dead letter must survive untouched.
        await Task.Delay(TimeSpan.FromSeconds(3));

        var row = (await fixture.GetOutboxRowsAsync()).Single(r => r.Payload.Contains(marker));
        await Assert.That(row.Attempts).IsEqualTo(BookingFixture.OutboxMaxAttempts);
        await Assert.That(row.Error!).Contains("permanently invalid");
    }

    [Test]
    public async Task Enqueue_InsideRolledBackTransaction_ShouldLeaveNoRowAndDispatchNothing()
    {
        await fixture.StartDispatcherAsync();

        fixture.ActAsNobody();
        var marker = Guid.NewGuid();

        await using (var scope = fixture.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<BookingContext>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

            await using var transaction = await unitOfWork.BeginTransactionAsync();
            await outbox.EnqueueAsync(new BookingCreatedNotification
            {
                BookingId = marker,
                RestaurantId = Guid.NewGuid(),
                CustomerName = "Doomed",
                CustomerEmail = "doomed@customers.demo",
                Date = DateTimeOffset.UtcNow.AddDays(1),
                PartySize = 2
            });
            await unitOfWork.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        // The row was never durable, so neither the hot path nor the sweep can ever see it.
        var markerText = marker.ToString();
        await Assert.That((await fixture.GetOutboxRowsAsync()).Any(r => r.Payload.Contains(markerText)))
            .IsFalse();

        // Outlast the grace period + a few sweeps: still nothing to recover.
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        await Assert.That((await fixture.GetOutboxRowsAsync()).Any(r => r.Payload.Contains(markerText)))
            .IsFalse();
    }

    private async Task<bool> BookingExistsAsync(Guid bookingId)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        return await unitOfWork.GetRepository<Booking>()
            .Query(q => q.Where(b => b.Id == bookingId))
            .AnyAsync();
    }
}
