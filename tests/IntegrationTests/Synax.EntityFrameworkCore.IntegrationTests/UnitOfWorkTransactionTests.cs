using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Proves that requesting a transaction while one is already active returns a non-owning handle:
/// disposing or committing the nested handle must not end the outer transaction.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class UnitOfWorkTransactionTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task BeginTransactionAsync_WhenNestedScopeDisposes_ShouldKeepOuterTransactionAlive()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var repo = unitOfWork.GetRepository<Booking>();

        await using var outer = await unitOfWork.BeginTransactionAsync();

        await repo.AddAsync(
            new Booking { Name = "NestedTx", Description = "before nested scope" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Simulates an inner method following the documented `await using` pattern.
        await using (var nested = await unitOfWork.BeginTransactionAsync())
        {
            await Assert.That(nested.TransactionId).IsEqualTo(outer.TransactionId);
        }

        // The nested scope's disposal must not have rolled back / ended the outer transaction.
        await Assert.That(dbContext.Database.CurrentTransaction).IsNotNull();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task BeginTransactionAsync_WhenNestedScopeCommits_ShouldNotCommitOuterTransaction()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        var repo = unitOfWork.GetRepository<Booking>();

        await using var outer = await unitOfWork.BeginTransactionAsync();

        await using (var nested = await unitOfWork.BeginTransactionAsync())
        {
            await repo.AddAsync(
                new Booking { Name = "NestedCommit", Description = "inner work" },
                CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            // A nested commit is a no-op; only the outermost owner completes the transaction.
            await nested.CommitAsync();
        }

        // The outer transaction is still in control: rolling it back discards the inner work.
        await unitOfWork.RollbackTransactionAsync();

        var count = await repo.Query(q => q.Where(b => b.Name == "NestedCommit")).CountAsync();
        await Assert.That(count).IsEqualTo(0);
    }
}
