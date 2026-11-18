using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class OwnershipTests(BookingDatabaseFixture fixture)
{
    // --------- Interceptor: auto-set OwnerId on add ---------- //

    [Test]
    public async Task AddAsync_WhenHasOwner_ShouldAutoSetOwnerId()
    {
        fixture.OwnerProvider.CurrentOwnerId = 10;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        var entity = new OwnedBooking { Name = "AutoOwned" };
        await repo.AddAsync(entity, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await Assert.That(entity.OwnerId).IsEqualTo(10);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task AddAsync_WhenHasOwner_AndOwnerIdSetExplicitly_ShouldNotOverwrite()
    {
        fixture.OwnerProvider.CurrentOwnerId = 10;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        var entity = new OwnedBooking { Name = "ExplicitOwner", OwnerId = 99 };
        await repo.AddAsync(entity, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await Assert.That(entity.OwnerId).IsEqualTo(99);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task AddAsync_WhenHasOwner_AndNoCurrentOwner_ShouldThrow()
    {
        fixture.OwnerProvider.CurrentOwnerId = null;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        var entity = new OwnedBooking { Name = "NoOwner" };
        await repo.AddAsync(entity, CancellationToken.None);

        var act = async () =>
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.SaveChangesAsync();
        };

        await Assert.That(act).ThrowsExactly<InvalidOperationException>();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task AddAsync_WhenHasOptionalOwner_ShouldAutoSetOwnerId()
    {
        fixture.OwnerProvider.CurrentOwnerId = 20;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OptionallyOwnedBooking>();

        var entity = new OptionallyOwnedBooking { Name = "OptionalAutoOwned" };
        await repo.AddAsync(entity, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await Assert.That(entity.OwnerId).IsEqualTo(20);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task AddAsync_WhenHasOptionalOwner_AndNoCurrentOwner_ShouldLeaveNull()
    {
        fixture.OwnerProvider.CurrentOwnerId = null;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OptionallyOwnedBooking>();

        var entity = new OptionallyOwnedBooking { Name = "SharedEntity" };
        await repo.AddAsync(entity, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await Assert.That(entity.OwnerId).IsNull();

        await unitOfWork.RollbackTransactionAsync();
    }

    // --------- Query filter: IHasOwner scoping ---------- //

    [Test]
    public async Task Query_WhenHasOwner_ShouldReturnOnlyMatchingOwner()
    {
        fixture.OwnerProvider.CurrentOwnerId = 1;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        await repo.AddAsync(
            new OwnedBooking { Name = "Owner1A", OwnerId = 1 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repo.AddAsync(
            new OwnedBooking { Name = "Owner1B", OwnerId = 1 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repo.AddAsync(
            new OwnedBooking { Name = "Owner2", OwnerId = 2 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var results = await repo.GetListAsync();

        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results.All(r => r.OwnerId == 1)).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task Query_WhenHasOwner_AndNoCurrentOwner_ShouldReturnNothing()
    {
        fixture.OwnerProvider.CurrentOwnerId = 1;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        await repo.AddAsync(
            new OwnedBooking { Name = "Owned", OwnerId = 1 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Switch to no owner
        fixture.OwnerProvider.CurrentOwnerId = null;

        var results = await repo.GetListAsync();

        await Assert.That(results).Count().IsEqualTo(0);

        await unitOfWork.RollbackTransactionAsync();
    }

    // --------- Query filter: IHasOptionalOwner scoping ---------- //

    [Test]
    public async Task Query_WhenHasOptionalOwner_ShouldReturnOwnedAndShared()
    {
        fixture.OwnerProvider.CurrentOwnerId = 5;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OptionallyOwnedBooking>();

        await repo.AddAsync(
            new OptionallyOwnedBooking { Name = "OwnedBy5", OwnerId = 5 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        // Insert shared row with no ambient owner so the interceptor leaves OwnerId null.
        fixture.OwnerProvider.CurrentOwnerId = null;
        await repo.AddAsync(
            new OptionallyOwnedBooking { Name = "Shared", OwnerId = null }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        fixture.OwnerProvider.CurrentOwnerId = 5;
        await repo.AddAsync(
            new OptionallyOwnedBooking { Name = "OwnedBy99", OwnerId = 99 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var results = await repo.GetListAsync();

        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results.Any(r => r.Name == "OwnedBy5")).IsTrue();
        await Assert.That(results.Any(r => r.Name == "Shared")).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task Query_WhenHasOptionalOwner_AndNoCurrentOwner_ShouldReturnOnlyShared()
    {
        fixture.OwnerProvider.CurrentOwnerId = 5;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OptionallyOwnedBooking>();

        await repo.AddAsync(
            new OptionallyOwnedBooking { Name = "OwnedBy5", OwnerId = 5 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        // Insert shared row with no ambient owner so the interceptor leaves OwnerId null.
        fixture.OwnerProvider.CurrentOwnerId = null;
        await repo.AddAsync(
            new OptionallyOwnedBooking { Name = "Shared", OwnerId = null }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var results = await repo.GetListAsync();

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Name).IsEqualTo("Shared");

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task Query_WhenIgnoreQueryFilters_ShouldReturnAll()
    {
        fixture.OwnerProvider.CurrentOwnerId = 1;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        await repo.AddAsync(
            new OwnedBooking { Name = "Owner1", OwnerId = 1 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repo.AddAsync(
            new OwnedBooking { Name = "Owner2", OwnerId = 2 }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var results = await repo
            .Query(q => q.IgnoreQueryFilters())
            .ToListAsync();

        await Assert.That(results).Count().IsEqualTo(2);

        await unitOfWork.RollbackTransactionAsync();
    }

    // --------- Cross-owner scoping ---------- //

    [Test]
    public async Task Query_WhenCrossOwner_ShouldReturnEveryOwnersRowsInSql()
    {
        fixture.OwnerProvider.CurrentOwnerId = 1;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        try
        {
            await repo.AddAsync(
                new OwnedBooking { Name = "CrossOwner1", OwnerId = 1 }, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await repo.AddAsync(
                new OwnedBooking { Name = "CrossOwner2", OwnerId = 2 }, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            // The cross-owner accessor is a DbContext member like the owner accessor, so EF
            // re-evaluates it per execution instead of baking the first value into cached SQL.
            await Assert.That(await repo.GetListAsync()).Count().IsEqualTo(1);

            fixture.OwnerProvider.CrossOwner = true;

            var results = await repo.GetListAsync();

            await Assert.That(results).Count().IsEqualTo(2);
        }
        finally
        {
            fixture.OwnerProvider.CrossOwner = false;
            await unitOfWork.RollbackTransactionAsync();
        }
    }

    [Test]
    public async Task Query_WhenCrossOwner_ShouldStillHideSoftDeletedRows()
    {
        fixture.OwnerProvider.CurrentOwnerId = 1;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedSoftDeletableBooking>();

        try
        {
            var kept = new OwnedSoftDeletableBooking { Name = "CrossKept", OwnerId = 2 };
            var deleted = new OwnedSoftDeletableBooking { Name = "CrossDeleted", OwnerId = 2 };
            await repo.AddAsync(kept, CancellationToken.None);
            await repo.AddAsync(deleted, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            repo.Remove(deleted);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            fixture.OwnerProvider.CrossOwner = true;

            // Lifting owner scoping widens one filter, not all of them.
            var results = await repo.GetListAsync();

            await Assert.That(results).Count().IsEqualTo(1);
            await Assert.That(results.Single().Name).IsEqualTo("CrossKept");
        }
        finally
        {
            fixture.OwnerProvider.CrossOwner = false;
            await unitOfWork.RollbackTransactionAsync();
        }
    }

    [Test]
    public async Task AddAsync_WhenCrossOwner_AndNoExplicitOwner_ShouldThrow()
    {
        fixture.OwnerProvider.CurrentOwnerId = 1;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<OwnedBooking>();

        try
        {
            fixture.OwnerProvider.CrossOwner = true;
            await repo.AddAsync(new OwnedBooking { Name = "CrossWrite" }, CancellationToken.None);

            var act = async () =>
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                await dbContext.SaveChangesAsync();
            };

            await Assert.That(act).ThrowsExactly<InvalidOperationException>()
                .WithMessageContaining("BecomeAllOwners");
        }
        finally
        {
            fixture.OwnerProvider.CrossOwner = false;
            await unitOfWork.RollbackTransactionAsync();
        }
    }
}
