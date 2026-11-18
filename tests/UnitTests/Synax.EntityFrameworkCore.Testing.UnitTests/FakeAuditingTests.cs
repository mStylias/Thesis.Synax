namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// Covers the fake's auditing parity with the library's <c>AuditingInterceptor</c> semantics:
/// creation stamps on add (explicit values win), modification stamps on update, no modification
/// stamp on soft delete, and creation stamps on bulk insert.
/// </summary>
public class FakeAuditingTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 5, 9, 0, 0, TimeSpan.Zero);

    private static FakeUnitOfWork<TestDbContext> CreateUow(Guid? userId = null)
        => new(new FakeUnitOfWorkOptions
        {
            TimeProvider = new FixedTimeProvider(FixedNow),
            CurrentUserId = () => userId
        });

    [Test]
    public async Task AddAndSave_StampsCreatedAtAndCreatedById()
    {
        var userId = Guid.NewGuid();
        var uow = CreateUow(userId);
        var widget = new AuditedWidget { Name = "new" };

        await uow.GetRepository<AuditedWidget>().AddAsync(widget);
        await uow.SaveChangesAsync();

        await Assert.That(widget.CreatedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(widget.CreatedById).IsEqualTo(userId);
        await Assert.That(widget.UpdatedAt).IsNull();
        await Assert.That(widget.ModifiedById).IsNull();
    }

    [Test]
    public async Task AddAndSave_WithExplicitCreatedAt_KeepsExplicitValue()
    {
        var backdated = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var uow = CreateUow();
        var widget = new AuditedWidget { Name = "imported", CreatedAt = backdated };

        await uow.GetRepository<AuditedWidget>().AddAsync(widget);
        await uow.SaveChangesAsync();

        await Assert.That(widget.CreatedAt).IsEqualTo(backdated);
    }

    [Test]
    public async Task UpdateAndSave_StampsUpdatedAtAndModifiedById_LeavesCreationUntouched()
    {
        var userId = Guid.NewGuid();
        var created = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var creator = Guid.NewGuid();
        var widget = new AuditedWidget { Name = "old", CreatedAt = created, CreatedById = creator };
        var uow = CreateUow(userId).Seed(widget);

        widget.Name = "new";
        uow.GetRepository<AuditedWidget>().Update(widget);
        await uow.SaveChangesAsync();

        await Assert.That(widget.UpdatedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(widget.ModifiedById).IsEqualTo(userId);
        await Assert.That(widget.CreatedAt).IsEqualTo(created);
        await Assert.That(widget.CreatedById).IsEqualTo(creator);
    }

    [Test]
    public async Task RemoveAndSave_OnSoftDeleteEntity_DoesNotStampModification()
    {
        var widget = new AuditedSoftWidget { Name = "target" };
        var uow = CreateUow(Guid.NewGuid()).Seed(widget);

        uow.GetRepository<AuditedSoftWidget>().Remove(widget);
        await uow.SaveChangesAsync();

        await Assert.That(widget.IsDeleted).IsTrue();
        await Assert.That(widget.UpdatedAt).IsNull();
    }

    [Test]
    public async Task BulkInsertAsync_StampsCreation()
    {
        var userId = Guid.NewGuid();
        var uow = CreateUow(userId);

        await uow.GetRepository<AuditedWidget>().BulkInsertAsync(
        [
            new AuditedWidget { Name = "a" },
            new AuditedWidget { Name = "b" }
        ]);

        var entities = uow.Entities<AuditedWidget>();
        await Assert.That(entities.All(w => w.CreatedAt == FixedNow.UtcDateTime)).IsTrue();
        await Assert.That(entities.All(w => w.CreatedById == userId)).IsTrue();
    }

    [Test]
    public async Task AddAndSave_WithoutUser_StampsTimeOnly()
    {
        var uow = CreateUow(userId: null);
        var widget = new AuditedWidget { Name = "anonymous" };

        await uow.GetRepository<AuditedWidget>().AddAsync(widget);
        await uow.SaveChangesAsync();

        await Assert.That(widget.CreatedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(widget.CreatedById).IsNull();
    }
}
