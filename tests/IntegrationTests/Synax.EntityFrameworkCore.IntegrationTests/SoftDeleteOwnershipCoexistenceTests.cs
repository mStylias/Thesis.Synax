using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Proves that an entity implementing both <c>ISoftDelete</c> and <c>IHasOwner&lt;TKey&gt;</c>
/// gets both query filters applied. EF Core forbids mixing anonymous and named query filters
/// on the same entity, so both Synax filters must be named.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class SoftDeleteOwnershipCoexistenceTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task Query_WhenSoftDeletableAndOwned_ShouldApplyBothFilters()
    {
        fixture.OwnerProvider.CurrentOwnerId = 1;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedSoftDeletableBooking>();

        var visible = new OwnedSoftDeletableBooking { Name = "Owner1Active", OwnerId = 1 };
        var deleted = new OwnedSoftDeletableBooking { Name = "Owner1Deleted", OwnerId = 1 };
        var otherOwner = new OwnedSoftDeletableBooking { Name = "Owner2Active", OwnerId = 2 };
        await repo.AddAsync(visible, CancellationToken.None);
        await repo.AddAsync(deleted, CancellationToken.None);
        await repo.AddAsync(otherOwner, CancellationToken.None);
        await unitOfWork.SaveChangesAsync();

        repo.Remove(deleted);
        await unitOfWork.SaveChangesAsync();

        var results = await repo.GetListAsync();

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Name).IsEqualTo("Owner1Active");

        var unfiltered = await repo
            .Query(q => q.IgnoreQueryFilters())
            .ToListAsync();

        await Assert.That(unfiltered).Count().IsEqualTo(3);

        await unitOfWork.RollbackTransactionAsync();
    }
}
