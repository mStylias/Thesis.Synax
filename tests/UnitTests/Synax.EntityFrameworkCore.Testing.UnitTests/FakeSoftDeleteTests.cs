using Microsoft.EntityFrameworkCore;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// Covers the fake's soft-delete parity with the library: query filters hide deleted rows,
/// <c>IgnoreQueryFilters</c> reveals them, and Remove converts to a stamped soft delete.
/// </summary>
public class FakeSoftDeleteTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Query_ByDefault_HidesSoftDeletedRows()
    {
        var uow = new FakeUnitOfWork<TestDbContext>().Seed(
            new SoftWidget { Name = "live" },
            new SoftWidget { Name = "gone", IsDeleted = true });

        var result = await uow.GetRepository<SoftWidget>().Query().ToListAsync();

        await Assert.That(result.Select(w => w.Name)).IsEquivalentTo(["live"]);
    }

    [Test]
    public async Task Query_WithIgnoreQueryFilters_RevealsSoftDeletedRows()
    {
        var uow = new FakeUnitOfWork<TestDbContext>().Seed(
            new SoftWidget { Name = "live" },
            new SoftWidget { Name = "gone", IsDeleted = true });

        var result = await uow.GetRepository<SoftWidget>()
            .Query(q => q.IgnoreQueryFilters())
            .ToListAsync();

        await Assert.That(result).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Query_WithFiltersDisabledInOptions_RevealsSoftDeletedRows()
    {
        var uow = new FakeUnitOfWork<TestDbContext>(new FakeUnitOfWorkOptions { ApplyQueryFilters = false })
            .Seed(new SoftWidget { Name = "gone", IsDeleted = true });

        var result = await uow.GetRepository<SoftWidget>().Query().ToListAsync();

        await Assert.That(result).Count().IsEqualTo(1);
    }

    [Test]
    public async Task RemoveAndSave_ConvertsToSoftDeleteWithTimestampAndUser()
    {
        var userId = Guid.NewGuid();
        var options = new FakeUnitOfWorkOptions
        {
            TimeProvider = new FixedTimeProvider(FixedNow),
            CurrentUserId = () => userId
        };
        var widget = new SoftWidget { Name = "target" };
        var uow = new FakeUnitOfWork<TestDbContext>(options).Seed(widget);

        uow.GetRepository<SoftWidget>().Remove(widget);
        await uow.SaveChangesAsync();

        // The row is soft deleted, not removed from the committed store.
        await Assert.That(uow.Entities<SoftWidget>()).Count().IsEqualTo(1);
        await Assert.That(widget.IsDeleted).IsTrue();
        await Assert.That(widget.DeletedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(widget.DeletedById).IsEqualTo(userId);
    }

    [Test]
    public async Task HardDeleteAndSave_RemovesRowPermanently()
    {
        var widget = new SoftWidget { Name = "target" };
        var uow = new FakeUnitOfWork<TestDbContext>().Seed(widget);

        uow.GetRepository<SoftWidget>().HardDelete(widget);
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<SoftWidget>()).IsEmpty();
    }

    [Test]
    public async Task ExecuteDeleteAsync_OnSoftDeleteEntity_ConvertsToUpdate()
    {
        var uow = new FakeUnitOfWork<TestDbContext>(new FakeUnitOfWorkOptions
        {
            TimeProvider = new FixedTimeProvider(FixedNow)
        }).Seed(
            new SoftWidget { Name = "a" },
            new SoftWidget { Name = "b" },
            new SoftWidget { Name = "keep" });

        var affected = await uow.GetRepository<SoftWidget>()
            .ExecuteDeleteAsync(q => q.Where(w => w.Name != "keep"));

        await Assert.That(affected).IsEqualTo(2);
        await Assert.That(uow.Entities<SoftWidget>()).Count().IsEqualTo(3);
        await Assert.That(uow.Entities<SoftWidget>().Count(w => w.IsDeleted)).IsEqualTo(2);
        await Assert.That(uow.Entities<SoftWidget>().Single(w => w.Name == "keep").IsDeleted).IsFalse();
    }

    [Test]
    public async Task ExecuteHardDeleteAsync_RemovesRowsPermanently()
    {
        var uow = new FakeUnitOfWork<TestDbContext>().Seed(
            new SoftWidget { Name = "a" },
            new SoftWidget { Name = "keep" });

        var affected = await uow.GetRepository<SoftWidget>()
            .ExecuteHardDeleteAsync(q => q.Where(w => w.Name == "a"));

        await Assert.That(affected).IsEqualTo(1);
        await Assert.That(uow.Entities<SoftWidget>().Select(w => w.Name)).IsEquivalentTo(["keep"]);
    }

    [Test]
    public async Task ExecuteDeleteAsync_OnNonSoftDeleteEntity_RemovesRows()
    {
        var uow = new FakeUnitOfWork<TestDbContext>().Seed(
            new Widget { Name = "a", Value = 1 },
            new Widget { Name = "b", Value = 2 });

        var affected = await uow.GetRepository<Widget>()
            .ExecuteDeleteAsync(q => q.Where(w => w.Value == 1));

        await Assert.That(affected).IsEqualTo(1);
        await Assert.That(uow.Entities<Widget>().Select(w => w.Name)).IsEquivalentTo(["b"]);
    }

    [Test]
    public async Task GetByIdAsync_HidesSoftDeletedRow_UnlessFiltersDisabled()
    {
        var widget = new SoftWidget { Name = "gone", IsDeleted = true };

        var filtered = new FakeUnitOfWork<TestDbContext>().Seed(widget);
        await Assert.That(await filtered.GetRepository<SoftWidget>().GetByIdAsync(widget.Id)).IsNull();

        var unfiltered = new FakeUnitOfWork<TestDbContext>(new FakeUnitOfWorkOptions { ApplyQueryFilters = false })
            .Seed(widget);
        await Assert.That(await unfiltered.GetRepository<SoftWidget>().GetByIdAsync(widget.Id)).IsNotNull();
    }
}
