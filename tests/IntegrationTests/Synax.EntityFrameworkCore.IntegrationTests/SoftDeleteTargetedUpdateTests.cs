using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Proves that the soft delete conversion updates only the soft-delete columns:
/// deleting a stale in-memory copy must not overwrite other columns that were
/// changed elsewhere in the meantime.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class SoftDeleteTargetedUpdateTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task Remove_WhenEntityCopyIsStale_ShouldNotOverwriteOtherColumns()
    {
        int id;

        // Insert and commit the row.
        await using (var scope = fixture.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
            var repo = unitOfWork.GetRepository<ConcurrentSoftDeleteBooking>();

            var entity = new ConcurrentSoftDeleteBooking { Name = "Current" };
            await repo.AddAsync(entity, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            id = entity.Id;
        }

        try
        {
            // Soft-delete via a stale detached copy whose Name differs from the stored value.
            await using (var scope = fixture.CreateScope())
            {
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
                var repo = unitOfWork.GetRepository<ConcurrentSoftDeleteBooking>();

                var staleCopy = new ConcurrentSoftDeleteBooking { Id = id, Name = "Stale" };
                repo.Remove(staleCopy);
                await unitOfWork.SaveChangesAsync();
            }

            // The row is soft-deleted, but Name kept the stored value.
            await using (var scope = fixture.CreateScope())
            {
                var repo = scope.ServiceProvider
                    .GetRequiredService<IUnitOfWork<TestDbContext>>()
                    .GetRepository<ConcurrentSoftDeleteBooking>();

                var row = await repo
                    .Query(q => q.IgnoreQueryFilters().Where(b => b.Id == id))
                    .SingleAsync();

                await Assert.That(row.IsDeleted).IsTrue();
                await Assert.That(row.Name).IsEqualTo("Current");
            }
        }
        finally
        {
            // Committed data: clean up so reruns start fresh.
            await using var scope = fixture.CreateScope();
            var repo = scope.ServiceProvider
                .GetRequiredService<IUnitOfWork<TestDbContext>>()
                .GetRepository<ConcurrentSoftDeleteBooking>();
            await repo.ExecuteHardDeleteAsync(q => q.IgnoreQueryFilters().Where(b => b.Id == id));
        }
    }
}
