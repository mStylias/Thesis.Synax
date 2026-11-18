using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Proves that soft delete timestamps come from the DI-registered <see cref="TimeProvider"/>
/// (deterministic in tests) rather than <c>DateTime.UtcNow</c>, on both the interceptor path
/// and the bulk <c>ExecuteDeleteAsync</c> path.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class SoftDeleteTimeProviderTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task RemoveAndSaveChangesAsync_ShouldStampDeletedAtFromTimeProvider()
    {
        fixture.UserProvider.CurrentUserId = 77;
        await using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<AuditedSoftDeletableBooking>();

        dbContext.TestUsers.Add(new TestUser { Id = 77, Name = "TestUser77" });
        await dbContext.SaveChangesAsync();

        var entity = new AuditedSoftDeletableBooking { Name = "FixedTimeTracked" };
        await repo.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        repo.Remove(entity);
        await unitOfWork.SaveChangesAsync();

        await Assert.That(entity.DeletedAt).IsEqualTo(fixture.TimeProvider.UtcNow.UtcDateTime);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ExecuteDeleteAsync_ShouldStampDeletedAtFromTimeProvider()
    {
        fixture.UserProvider.CurrentUserId = 78;
        await using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<AuditedSoftDeletableBooking>();

        dbContext.TestUsers.Add(new TestUser { Id = 78, Name = "TestUser78" });
        await dbContext.SaveChangesAsync();

        var entity = new AuditedSoftDeletableBooking { Name = "FixedTimeBulk" };
        await repo.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        await repo.ExecuteDeleteAsync(q => q.Where(b => b.Id == entity.Id));

        var deleted = await repo
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Id == entity.Id))
            .SingleAsync();

        await Assert.That(deleted.IsDeleted).IsTrue();
        await Assert.That(deleted.DeletedAt).IsEqualTo(fixture.TimeProvider.UtcNow.UtcDateTime);

        await unitOfWork.RollbackTransactionAsync();
    }
}
