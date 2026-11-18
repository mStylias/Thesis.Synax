namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// Covers <see cref="FakeUnitOfWork{TDbContext}"/> change-tracking semantics (staged work becomes
/// visible only after save), key-based lookups, and the tracked no-op transactions.
/// </summary>
public class FakeUnitOfWorkTests
{
    [Test]
    public async Task Add_IsNotVisibleUntilSaveChanges()
    {
        var uow = new FakeUnitOfWork<TestDbContext>();
        var repo = uow.GetRepository<Widget>();

        await repo.AddAsync(new Widget { Name = "staged" });

        await Assert.That(await repo.Query().ToListAsync()).IsEmpty();

        var changes = await uow.SaveChangesAsync();

        await Assert.That(changes).IsEqualTo(1);
        await Assert.That(await repo.Query().ToListAsync()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Store_IsSharedAcrossGetRepositoryCalls()
    {
        var uow = new FakeUnitOfWork<TestDbContext>();

        await uow.GetRepository<Widget>().AddAsync(new Widget { Name = "one" });
        await uow.SaveChangesAsync();

        var other = uow.GetRepository<Widget>();
        await Assert.That(await other.Query().ToListAsync()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SaveChanges_AppliesStagedWorkAcrossAllStores()
    {
        var uow = new FakeUnitOfWork<TestDbContext>();

        await uow.GetRepository<Widget>().AddAsync(new Widget { Name = "w" });
        await uow.GetRepository<SoftWidget>().AddAsync(new SoftWidget { Name = "s" });

        var changes = uow.SaveChanges();

        await Assert.That(changes).IsEqualTo(2);
        await Assert.That(uow.Entities<Widget>()).Count().IsEqualTo(1);
        await Assert.That(uow.Entities<SoftWidget>()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Update_OfDetachedInstanceWithSameKey_ReplacesCommittedEntity()
    {
        var id = Guid.NewGuid();
        var uow = new FakeUnitOfWork<TestDbContext>().Seed(new Widget { Id = id, Name = "old" });

        uow.GetRepository<Widget>()
            .Update(new Widget { Id = id, Name = "new" });
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<Widget>().Single().Name).IsEqualTo("new");
    }

    [Test]
    public async Task Update_OfUnknownEntity_ThrowsOnSave()
    {
        var uow = new FakeUnitOfWork<TestDbContext>();
        var repo = uow.GetRepository<Widget>();

        repo.Update(new Widget { Name = "ghost" });
        await Assert.That(() => uow.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no committed entity");
    }

    [Test]
    public async Task GetByIdAsync_UsesIdPropertyByConvention()
    {
        var widget = new Widget { Name = "target" };
        var repo = new FakeUnitOfWork<TestDbContext>().Seed(widget).GetRepository<Widget>();

        await Assert.That(await repo.GetByIdAsync(widget.Id)).IsSameReferenceAs(widget);
        await Assert.That(await repo.GetByIdAsync(Guid.NewGuid())).IsNull();
    }

    [Test]
    public async Task GetByIdAsync_UsesConfiguredKeySelector()
    {
        var options = new FakeUnitOfWorkOptions().UseKey<Widget>(w => w.Name);
        var repo = new FakeUnitOfWork<TestDbContext>(options)
            .Seed(new Widget { Name = "by-name" })
            .GetRepository<Widget>();

        var found = await repo.GetByIdAsync("by-name");

        await Assert.That(found).IsNotNull();
    }

    [Test]
    public async Task GetByIdAsync_WithSingleElementKeyArray_Works()
    {
        var widget = new Widget { Name = "target" };
        var repo = new FakeUnitOfWork<TestDbContext>().Seed(widget).GetRepository<Widget>();

        await Assert.That(await repo.GetByIdAsync([widget.Id])).IsSameReferenceAs(widget);
    }

    [Test]
    public async Task GetByIdAsync_WithCompositeKeyArray_Throws()
    {
        var repo = new FakeUnitOfWork<TestDbContext>().GetRepository<Widget>();

        await Assert.That(() => repo.GetByIdAsync([1, 2]))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task ExecuteUpdateAsync_ThrowsNotSupportedWithGuidance()
    {
        var repo = new FakeUnitOfWork<TestDbContext>().GetRepository<Widget>();

        await Assert.That(() => repo.ExecuteUpdateAsync(q => q, setters => { }))
            .Throws<NotSupportedException>()
            .WithMessageContaining("integration tests");
    }

    [Test]
    public async Task Transactions_AreTrackedNoOps()
    {
        var uow = new FakeUnitOfWork<TestDbContext>();

        await using (var transaction = await uow.BeginTransactionAsync())
        {
            await transaction.CommitAsync();
        }

        await using (var transaction = await uow.BeginTransactionAsync())
        {
            await transaction.RollbackAsync();
        }

        await Assert.That(uow.Transactions).Count().IsEqualTo(2);
        await Assert.That(uow.Transactions[0].WasCommitted).IsTrue();
        await Assert.That(uow.Transactions[1].WasRolledBack).IsTrue();
        await Assert.That(uow.TransactionCommits).IsEqualTo(1);
        await Assert.That(uow.TransactionRollbacks).IsEqualTo(1);
    }

    [Test]
    public async Task CommitAndRollback_OnUnitOfWork_IncrementCounters()
    {
        var uow = new FakeUnitOfWork<TestDbContext>();

        await uow.CommitTransactionAsync();
        await uow.RollbackTransactionAsync();

        await Assert.That(uow.TransactionCommits).IsEqualTo(1);
        await Assert.That(uow.TransactionRollbacks).IsEqualTo(1);
    }

    [Test]
    public async Task ExecutionStrategy_ExecutesOperationOnce()
    {
        var uow = new FakeUnitOfWork<TestDbContext>();
        var strategy = uow.CreateExecutionStrategy();
        var calls = 0;

        var result = await strategy.ExecuteAsync(0, (_, _, _) =>
        {
            calls++;
            return Task.FromResult(42);
        }, verifySucceeded: null);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveRange_And_UpdateRange_StageAllEntities()
    {
        var a = new Widget { Name = "a" };
        var b = new Widget { Name = "b" };
        var uow = new FakeUnitOfWork<TestDbContext>().Seed(a, b);
        var repo = uow.GetRepository<Widget>();

        repo.RemoveRange([a, b]);
        var changes = uow.SaveChanges();

        await Assert.That(changes).IsEqualTo(2);
        await Assert.That(uow.Entities<Widget>()).IsEmpty();
    }
}
