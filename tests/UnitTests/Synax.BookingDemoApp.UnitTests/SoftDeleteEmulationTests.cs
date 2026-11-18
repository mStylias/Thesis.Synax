using Microsoft.EntityFrameworkCore;
using Synax.BookingDemoApp.Application.Features.Tables;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.UnitTests.TestSupport;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: the fake's soft-delete emulation — <c>Remove</c> converts to a stamped soft delete
/// with the library's own semantics (<c>IsDeleted</c>/<c>DeletedAt</c> from the fake's
/// <see cref="TimeProvider"/>), the emulated query filter hides the row, and
/// <c>IgnoreQueryFilters()</c> reveals it — driven through the real <see cref="DeleteTableHandler"/>.
/// </summary>
public class SoftDeleteEmulationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 6, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    [Test]
    public async Task DeleteTable_SoftDeletesWithTimestamp_AndDisappearsFromFilteredQueries()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant, clock: new FixedTimeProvider(FixedNow));
        var table = new Table { Id = Guid.NewGuid(), Number = 7, Seats = 2, OwnerId = Tenant };
        uow.Seed(table);
        var handler = new DeleteTableHandler(uow);

        var result = await handler.HandleAsync(new DeleteTableRequest { Id = table.Id }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();

        // The row was soft deleted, not removed — stamped by the emulated interceptor.
        var committed = uow.Entities<Table>().Single();
        await Assert.That(committed.IsDeleted).IsTrue();
        await Assert.That(committed.DeletedAt).IsEqualTo(FixedNow.UtcDateTime);

        // The emulated soft-delete query filter now hides it...
        var visible = await uow.GetRepository<Table>().Query().ToListAsync();
        await Assert.That(visible).IsEmpty();

        // ...and IgnoreQueryFilters reveals it, exactly like the real filters.
        var unfiltered = await uow.GetRepository<Table>()
            .Query(q => q.IgnoreQueryFilters())
            .ToListAsync();
        await Assert.That(unfiltered).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DeleteTable_UnknownId_FailsWithNotFound_AndLogBreadcrumb()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant);
        var handler = new DeleteTableHandler(uow);
        var id = Guid.NewGuid();

        var result = await handler.HandleAsync(new DeleteTableRequest { Id = id }, CancellationToken.None);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo(ErrorCodes.Tables.NotFound);
        // Server-side message breadcrumb attached with WithMessage — never serialized to clients.
        await Assert.That(result.Messages).Contains($"Table {id} not found for deletion.");
    }
}
