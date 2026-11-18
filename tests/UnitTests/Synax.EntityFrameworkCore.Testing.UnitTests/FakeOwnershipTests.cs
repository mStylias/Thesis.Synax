using Microsoft.EntityFrameworkCore;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// Covers the fake's ownership parity with the library: required/optional owner query filters,
/// owner stamping on tracked adds and bulk inserts, and the no-ambient-owner failure mode.
/// </summary>
public class FakeOwnershipTests
{
    private static FakeUnitOfWork<TestDbContext> CreateUowForOwner(int? ownerId)
        => new(new FakeUnitOfWorkOptions { CurrentOwnerId = () => ownerId });

    [Test]
    public async Task Query_RequiredOwner_ReturnsOnlyCurrentOwnersRows()
    {
        var uow = CreateUowForOwner(1).Seed(
            new OwnedWidget { Name = "mine", OwnerId = 1 },
            new OwnedWidget { Name = "theirs", OwnerId = 2 });

        var result = await uow.GetRepository<OwnedWidget>().Query().ToListAsync();

        await Assert.That(result.Select(w => w.Name)).IsEquivalentTo(["mine"]);
    }

    [Test]
    public async Task Query_RequiredOwner_WithNoAmbientOwner_ReturnsNothing()
    {
        var uow = CreateUowForOwner(null).Seed(new OwnedWidget { Name = "row", OwnerId = 1 });

        var result = await uow.GetRepository<OwnedWidget>().Query().ToListAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Query_RequiredOwner_IgnoreQueryFilters_ReturnsAllRows()
    {
        var uow = CreateUowForOwner(1).Seed(
            new OwnedWidget { Name = "mine", OwnerId = 1 },
            new OwnedWidget { Name = "theirs", OwnerId = 2 });

        var result = await uow.GetRepository<OwnedWidget>()
            .Query(q => q.IgnoreQueryFilters())
            .ToListAsync();

        await Assert.That(result).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Query_OptionalOwner_SharedRowsVisibleToEveryone()
    {
        var uow = CreateUowForOwner(1).Seed(
            new SharedWidget { Name = "shared", OwnerId = null },
            new SharedWidget { Name = "mine", OwnerId = 1 },
            new SharedWidget { Name = "theirs", OwnerId = 2 });

        var result = await uow.GetRepository<SharedWidget>().Query().ToListAsync();

        await Assert.That(result.Select(w => w.Name)).IsEquivalentTo(["shared", "mine"]);
    }

    [Test]
    public async Task AddAndSave_StampsAmbientOwner()
    {
        var uow = CreateUowForOwner(7);

        await uow.GetRepository<OwnedWidget>().AddAsync(new OwnedWidget { Name = "new" });
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<OwnedWidget>().Single().OwnerId).IsEqualTo(7);
    }

    [Test]
    public async Task AddAndSave_WithExplicitOwner_KeepsExplicitValue()
    {
        var uow = CreateUowForOwner(7);

        await uow.GetRepository<OwnedWidget>().AddAsync(new OwnedWidget { Name = "new", OwnerId = 3 });
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<OwnedWidget>().Single().OwnerId).IsEqualTo(3);
    }

    [Test]
    public async Task AddAndSave_RequiredOwnerWithoutAmbientOwner_Throws()
    {
        var uow = CreateUowForOwner(null);
        var repo = uow.GetRepository<OwnedWidget>();

        await repo.AddAsync(new OwnedWidget { Name = "new" });
        await Assert.That(() => uow.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no current owner");
    }

    // -------- Cross-owner scoping (BecomeAllOwners) --------

    private static FakeUnitOfWork<TestDbContext> CreateCrossOwnerUow(int? ownerId)
        => new(new FakeUnitOfWorkOptions { CurrentOwnerId = () => ownerId, CrossOwner = () => true });

    [Test]
    public async Task Query_CrossOwner_ReturnsEveryOwnersRows()
    {
        var uow = CreateCrossOwnerUow(1).Seed(
            new OwnedWidget { Name = "mine", OwnerId = 1 },
            new OwnedWidget { Name = "theirs", OwnerId = 2 });

        var result = await uow.GetRepository<OwnedWidget>().Query().ToListAsync();

        await Assert.That(result.Select(w => w.Name)).IsEquivalentTo(["mine", "theirs"]);
    }

    [Test]
    public async Task Query_CrossOwner_OptionalOwner_ReturnsSharedAndAllOwnedRows()
    {
        var uow = CreateCrossOwnerUow(1).Seed(
            new SharedWidget { Name = "shared", OwnerId = null },
            new SharedWidget { Name = "mine", OwnerId = 1 },
            new SharedWidget { Name = "theirs", OwnerId = 2 });

        var result = await uow.GetRepository<SharedWidget>().Query().ToListAsync();

        await Assert.That(result.Select(w => w.Name)).IsEquivalentTo(["shared", "mine", "theirs"]);
    }

    [Test]
    public async Task AddAndSave_CrossOwner_WithoutExplicitOwner_Throws()
    {
        // Cross-owner widens reads only — a write with no owner in scope must not guess one.
        var uow = CreateCrossOwnerUow(1);
        var repo = uow.GetRepository<OwnedWidget>();

        await repo.AddAsync(new OwnedWidget { Name = "new" });
        await Assert.That(() => uow.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("BecomeAllOwners");
    }

    [Test]
    public async Task AddAndSave_CrossOwner_WithExplicitOwner_Succeeds()
    {
        var uow = CreateCrossOwnerUow(1);

        await uow.GetRepository<OwnedWidget>().AddAsync(new OwnedWidget { Name = "new", OwnerId = 5 });
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<OwnedWidget>().Single().OwnerId).IsEqualTo(5);
    }

    [Test]
    public async Task BulkInsertAsync_StampsAmbientOwner()
    {
        var uow = CreateUowForOwner(9);

        await uow.GetRepository<OwnedWidget>().BulkInsertAsync(
        [
            new OwnedWidget { Name = "a" },
            new OwnedWidget { Name = "b", OwnerId = 4 }
        ]);

        var entities = uow.Entities<OwnedWidget>();
        await Assert.That(entities.Single(w => w.Name == "a").OwnerId).IsEqualTo(9);
        await Assert.That(entities.Single(w => w.Name == "b").OwnerId).IsEqualTo(4);
    }

    [Test]
    public async Task AddAndSave_OptionalOwner_StampsWhenUnset()
    {
        var uow = CreateUowForOwner(5);

        await uow.GetRepository<SharedWidget>().AddAsync(new SharedWidget { Name = "new" });
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<SharedWidget>().Single().OwnerId).IsEqualTo(5);
    }

    [Test]
    public async Task AddAndSave_OptionalOwner_NoAmbientOwner_StaysShared()
    {
        var uow = CreateUowForOwner(null);

        await uow.GetRepository<SharedWidget>().AddAsync(new SharedWidget { Name = "new" });
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<SharedWidget>().Single().OwnerId).IsNull();
    }
}
