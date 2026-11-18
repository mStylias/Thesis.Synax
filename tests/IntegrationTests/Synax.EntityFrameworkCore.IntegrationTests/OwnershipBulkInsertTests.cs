using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Proves that bulk insert applies the same owner assignment as the ownership interceptor,
/// even though it bypasses the change tracker.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class OwnershipBulkInsertTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task BulkInsertAsync_WhenHasOwner_ShouldAutoSetOwnerId()
    {
        fixture.OwnerProvider.CurrentOwnerId = 7;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        var bookings = Enumerable.Range(1, 20)
            .Select(i => new OwnedBooking { Name = $"BulkOwned {i}" })
            .ToList();

        await repo.BulkInsertAsync(bookings);

        await Assert.That(bookings.All(b => b.OwnerId == 7)).IsTrue();

        // Rows must be visible through the ownership query filter for owner 7.
        var visible = await repo.GetListAsync();
        await Assert.That(visible).Count().IsEqualTo(20);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task BulkInsertAsync_WhenHasOwner_AndOwnerIdSetExplicitly_ShouldNotOverwrite()
    {
        fixture.OwnerProvider.CurrentOwnerId = 7;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        await repo.BulkInsertAsync([new OwnedBooking { Name = "BulkExplicit", OwnerId = 42 }]);

        var rows = await repo
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Name == "BulkExplicit"))
            .ToListAsync();

        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0].OwnerId).IsEqualTo(42);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task BulkInsertAsync_WhenHasOwner_AndNoCurrentOwner_ShouldThrow()
    {
        fixture.OwnerProvider.CurrentOwnerId = null;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        var act = async () => await repo.BulkInsertAsync([new OwnedBooking { Name = "BulkNoOwner" }]);

        await Assert.That(act).ThrowsExactly<InvalidOperationException>();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task BulkInsertAsync_WhenHasOptionalOwner_ShouldAutoSetOwnerId()
    {
        fixture.OwnerProvider.CurrentOwnerId = 8;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OptionallyOwnedBooking>();

        var booking = new OptionallyOwnedBooking { Name = "BulkOptionalOwned" };
        await repo.BulkInsertAsync([booking]);

        await Assert.That(booking.OwnerId).IsEqualTo(8);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task BulkInsertAsync_WhenHasOptionalOwner_AndNoCurrentOwner_ShouldLeaveNull()
    {
        fixture.OwnerProvider.CurrentOwnerId = null;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OptionallyOwnedBooking>();

        var booking = new OptionallyOwnedBooking { Name = "BulkShared" };
        await repo.BulkInsertAsync([booking]);

        await Assert.That(booking.OwnerId).IsNull();

        await unitOfWork.RollbackTransactionAsync();
    }
}
