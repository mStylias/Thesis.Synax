using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.UnitTests.TestSupport;
using Synax.EntityFrameworkCore.Testing;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: first-class pagination on <c>ISynaxQuery</c> — offset paging with
/// <c>ToPagedListAsync</c> (page metadata + total count) and cursor-based keyset paging with
/// <c>ToKeysetPageAsync</c> (<c>NextCursor</c>/<c>HasMore</c>, no overlap between pages) — all
/// evaluated in memory by the Testing fakes over demo <see cref="Booking"/> entities.
/// </summary>
public class PaginationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static FakeUnitOfWork<BookingContext> CreateSeededUow(int count = 25)
        => TestWorld.CreateUow(tenant: Tenant).Seed(Enumerable.Range(1, count)
            .Select(i => new Booking
            {
                Id = Guid.NewGuid(),
                RestaurantId = Guid.NewGuid(),
                TableId = Guid.NewGuid(),
                CustomerName = $"Customer {i:D2}",
                CustomerEmail = $"c{i:D2}@example.com",
                Date = new DateTimeOffset(2026, 8, 1, 19, 0, 0, TimeSpan.Zero).AddDays(i),
                PartySize = i,
                Status = BookingStatus.Confirmed,
                OwnerId = Tenant
            })
            .ToArray());

    [Test]
    public async Task ToPagedListAsync_ReturnsTheRequestedWindowWithMetadata()
    {
        var repo = CreateSeededUow().GetRepository<Booking>();

        var page = await repo
            .Query(q => q.OrderBy(b => b.PartySize))
            .ToPagedListAsync(page: 2, pageSize: 10);

        await Assert.That(page.Items.Select(b => b.PartySize)).IsEquivalentTo(Enumerable.Range(11, 10));
        await Assert.That(page.Page).IsEqualTo(2);
        await Assert.That(page.TotalCount).IsEqualTo(25);
        await Assert.That(page.TotalPages).IsEqualTo(3);
        await Assert.That(page.HasPreviousPage).IsTrue();
        await Assert.That(page.HasNextPage).IsTrue();
    }

    [Test]
    public async Task ToKeysetPageAsync_PagesWithACursor_NoOverlapBetweenPages()
    {
        var repo = CreateSeededUow().GetRepository<Booking>();

        var first = await repo.Query().ToKeysetPageAsync(b => b.PartySize, pageSize: 10);

        await Assert.That(first.Items).Count().IsEqualTo(10);
        await Assert.That(first.HasMore).IsTrue();
        await Assert.That(first.NextCursor).IsEqualTo(10);   // the last key on the page

        var second = await repo.Query()
            .ToKeysetPageAsync(b => b.PartySize, pageSize: 10, after: first.NextCursor);

        await Assert.That(second.Items).Count().IsEqualTo(10);
        await Assert.That(second.Items.Select(b => b.PartySize)).IsEquivalentTo(Enumerable.Range(11, 10));
        // No booking appears on both pages — the cursor is exclusive.
        await Assert.That(first.Items.Select(b => b.Id).Intersect(second.Items.Select(b => b.Id))).IsEmpty();
    }
}
