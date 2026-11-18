using Synax.EntityFrameworkCore.Abstractions.Patching;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

public class WidgetPatch
{
    // Non-PatchValue members (route ids, flags) are not part of the patch surface.
    public Guid Id { get; set; }

    public PatchValue<string> Name { get; set; }
    public PatchValue<int> Value { get; set; }
}

public class AuditedWidgetPatch
{
    public PatchValue<string> Name { get; set; }
}

public class SoftWidgetPatch
{
    public PatchValue<string> Name { get; set; }
}

public class MismatchedPatch
{
    public PatchValue<string> NoSuchProperty { get; set; }
}

/// <summary>
/// Covers the fake's <c>ExecutePatchAsync</c> parity with the real executor: only set members
/// are applied, unset members leave data untouched, explicit null clears, modification audit
/// members are stamped, the matched row count is returned, and name mismatches fail
/// descriptively on first use.
/// </summary>
public class FakePatchTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 5, 9, 0, 0, TimeSpan.Zero);

    private static FakeUnitOfWork<TestDbContext> CreateUow(Guid? userId = null)
        => new(new FakeUnitOfWorkOptions
        {
            TimeProvider = new FixedTimeProvider(FixedNow),
            CurrentUserId = () => userId
        });

    [Test]
    public async Task ExecutePatch_AppliesSetMembers_AndLeavesUnsetUntouched()
    {
        var widget = new Widget { Name = "old", Value = 10 };
        var uow = CreateUow().Seed(widget);

        var affected = await uow.GetRepository<Widget>().ExecutePatchAsync(
            q => q.Where(w => w.Id == widget.Id),
            new WidgetPatch { Value = 99 });

        await Assert.That(affected).IsEqualTo(1);
        await Assert.That(widget.Value).IsEqualTo(99);
        await Assert.That(widget.Name).IsEqualTo("old");
    }

    [Test]
    public async Task ExecutePatch_SetToNull_ClearsTheMember()
    {
        var widget = new Widget { Name = "old" };
        var uow = CreateUow().Seed(widget);

        await uow.GetRepository<Widget>().ExecutePatchAsync(
            q => q.Where(w => w.Id == widget.Id),
            new WidgetPatch { Name = PatchValue<string>.Of(null) });

        await Assert.That(widget.Name).IsNull();
    }

    [Test]
    public async Task ExecutePatch_NoMatch_ReturnsZero()
    {
        var uow = CreateUow().Seed(new Widget { Name = "old" });

        var affected = await uow.GetRepository<Widget>().ExecutePatchAsync(
            q => q.Where(w => w.Id == Guid.NewGuid()),
            new WidgetPatch { Name = "new" });

        await Assert.That(affected).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutePatch_AllUnset_ReturnsMatchedCount_AndChangesNothing()
    {
        var widget = new AuditedWidget { Name = "old" };
        var uow = CreateUow(Guid.NewGuid()).Seed(widget);

        var affected = await uow.GetRepository<AuditedWidget>().ExecutePatchAsync(
            q => q.Where(w => w.Id == widget.Id),
            new AuditedWidgetPatch());

        await Assert.That(affected).IsEqualTo(1);
        await Assert.That(widget.Name).IsEqualTo("old");
        // An empty patch performs no update — no audit stamp either.
        await Assert.That(widget.UpdatedAt).IsNull();
        await Assert.That(widget.ModifiedById).IsNull();
    }

    [Test]
    public async Task ExecutePatch_StampsModificationAudit()
    {
        var userId = Guid.NewGuid();
        var widget = new AuditedWidget { Name = "old" };
        var uow = CreateUow(userId).Seed(widget);

        await uow.GetRepository<AuditedWidget>().ExecutePatchAsync(
            q => q.Where(w => w.Id == widget.Id),
            new AuditedWidgetPatch { Name = "new" });

        await Assert.That(widget.Name).IsEqualTo("new");
        await Assert.That(widget.UpdatedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(widget.ModifiedById).IsEqualTo(userId);
    }

    [Test]
    public async Task ExecutePatch_SoftDeletedRows_AreNotPatched()
    {
        // The soft-delete query filter scopes the patch like any other query.
        var deleted = new SoftWidget { Name = "old", IsDeleted = true };
        var uow = CreateUow().Seed(deleted);

        var affected = await uow.GetRepository<SoftWidget>().ExecutePatchAsync(
            q => q.Where(w => w.Id == deleted.Id),
            new SoftWidgetPatch { Name = "new" });

        await Assert.That(affected).IsEqualTo(0);
        await Assert.That(deleted.Name).IsEqualTo("old");
    }

    [Test]
    public async Task ExecutePatch_MemberWithoutEntityCounterpart_FailsDescriptively()
    {
        var uow = CreateUow().Seed(new Widget());

        await Assert.That(() => uow.GetRepository<Widget>().ExecutePatchAsync(
                q => q, new MismatchedPatch { NoSuchProperty = "x" }))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("NoSuchProperty");
    }
}
