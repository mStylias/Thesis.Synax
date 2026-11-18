using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Covers the <c>AuditingInterceptor</c> end to end: creation stamps on insert (explicit values
/// win), modification stamps on update, no modification stamp on soft delete, and creation stamps
/// on bulk insert. The user provider and TimeProvider are resolved from DI.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class AuditingTests(BookingDatabaseFixture fixture)
{
    private DateTime FixtureNow => fixture.TimeProvider.GetUtcNow().UtcDateTime;

    /// <summary>CreatedById/ModifiedById carry FKs to TestUsers; the referenced users must exist.</summary>
    private static async Task SeedUserAsync(AsyncServiceScope scope, params int[] userIds)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        foreach (var userId in userIds)
        {
            dbContext.TestUsers.Add(new TestUser { Id = userId, Name = $"AuditUser{userId}" });
        }

        await dbContext.SaveChangesAsync();
    }

    [Test]
    public async Task Add_StampsCreatedAtAndCreatedById()
    {
        fixture.AuditUserProvider.CurrentUserId = 11;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        await SeedUserAsync(scope, 11);
        var repo = unitOfWork.GetRepository<AuditedBooking>();

        var booking = new AuditedBooking { Name = "AuditAdd" };
        await repo.AddAsync(booking);
        await unitOfWork.SaveChangesAsync();

        await Assert.That(booking.CreatedAt).IsEqualTo(FixtureNow);
        await Assert.That(booking.CreatedById).IsEqualTo(11);
        await Assert.That(booking.UpdatedAt).IsNull();
        await Assert.That(booking.ModifiedById).IsNull();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task Add_WithExplicitCreatedAt_KeepsExplicitValue()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<AuditedBooking>();

        var backdated = new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var booking = new AuditedBooking { Name = "AuditBackdated", CreatedAt = backdated };
        await repo.AddAsync(booking);
        await unitOfWork.SaveChangesAsync();

        var stored = await repo.Query(q => q.Where(b => b.Id == booking.Id)).SingleAsync();
        await Assert.That(stored.CreatedAt).IsEqualTo(backdated);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task Update_StampsUpdatedAtAndModifiedById_LeavesCreationUntouched()
    {
        fixture.AuditUserProvider.CurrentUserId = 21;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        await SeedUserAsync(scope, 21, 22);
        var repo = unitOfWork.GetRepository<AuditedBooking>();

        var booking = new AuditedBooking { Name = "AuditUpdate" };
        await repo.AddAsync(booking);
        await unitOfWork.SaveChangesAsync();
        var createdAt = booking.CreatedAt;

        fixture.AuditUserProvider.CurrentUserId = 22;
        booking.Name = "AuditUpdate v2";
        repo.Update(booking);
        await unitOfWork.SaveChangesAsync();

        var stored = await repo.Query(q => q.Where(b => b.Id == booking.Id)).SingleAsync();
        await Assert.That(stored.UpdatedAt).IsEqualTo(FixtureNow);
        await Assert.That(stored.ModifiedById).IsEqualTo(22);
        await Assert.That(stored.CreatedAt).IsEqualTo(createdAt);
        await Assert.That(stored.CreatedById).IsEqualTo(21);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task SoftDelete_DoesNotStampModification()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<AuditTimesSoftDeleteBooking>();

        var booking = new AuditTimesSoftDeleteBooking { Name = "AuditSoftDelete" };
        await repo.AddAsync(booking);
        await unitOfWork.SaveChangesAsync();

        repo.Remove(booking);
        await unitOfWork.SaveChangesAsync();

        var stored = await repo
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Id == booking.Id))
            .SingleAsync();
        await Assert.That(stored.IsDeleted).IsTrue();
        await Assert.That(stored.UpdatedAt).IsNull();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task BulkInsert_StampsCreation()
    {
        fixture.AuditUserProvider.CurrentUserId = 31;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        await SeedUserAsync(scope, 31);
        var repo = unitOfWork.GetRepository<AuditedBooking>();

        var bookings = Enumerable.Range(1, 15)
            .Select(i => new AuditedBooking { Name = $"AuditBulk {i}" })
            .ToList();

        await repo.BulkInsertAsync(bookings);

        var stored = await repo.Query(q => q.Where(b => b.Name.StartsWith("AuditBulk"))).ToListAsync();
        await Assert.That(stored).Count().IsEqualTo(15);
        await Assert.That(stored.All(b => b.CreatedAt == FixtureNow)).IsTrue();
        await Assert.That(stored.All(b => b.CreatedById == 31)).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }
}
