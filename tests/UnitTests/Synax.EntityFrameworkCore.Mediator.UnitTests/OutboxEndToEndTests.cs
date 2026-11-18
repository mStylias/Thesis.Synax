using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator.UnitTests.TestFixtures.Outbox;

namespace Synax.EntityFrameworkCore.Mediator.UnitTests;

/// <summary>
/// End-to-end outbox tests over shared-cache in-memory Sqlite: real DbContext + interceptor,
/// real mediator, real dispatcher hosted service. Sweep-sensitive tests use a long
/// SweepInterval to isolate the hot path, and a tiny one to exercise recovery.
/// </summary>
public class OutboxEndToEndTests
{
    private static readonly TimeSpan SweepDisabled = TimeSpan.FromHours(1);

    private static async Task EnqueueAndSaveAsync(
        OutboxTestHarness harness, Synax.Mediator.INotification notification,
        Func<IUnitOfWork<OutboxTestDbContext>, Task>? beforeSave = null)
    {
        using var scope = harness.NewScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<OutboxTestDbContext>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<OutboxTestDbContext>>();

        await outbox.EnqueueAsync(notification);
        if (beforeSave is not null) await beforeSave(unitOfWork);
        await unitOfWork.SaveChangesAsync();
    }

    private static OutboxMessage<OutboxTestDbContext> RowFor(
        OutboxTestNotification notification, int attempts = 0, DateTimeOffset? nextAttemptAt = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            Type = typeof(OutboxTestNotification).FullName!,
            Payload = JsonSerializer.Serialize(notification),
            CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5),
            Attempts = attempts,
            NextAttemptAt = nextAttemptAt
        };

    // ---------- Hot path ----------

    [Test]
    public async Task Enqueue_SaveWithoutTransaction_ShouldDispatchAndDeleteRow()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o => o.SweepInterval = SweepDisabled);
        await harness.StartDispatcherAsync();
        var marker = Guid.NewGuid();

        await EnqueueAndSaveAsync(harness, new OutboxTestNotification(marker, "hello"));

        var dispatched = await OutboxTestHarness.PollUntilAsync(async () =>
            harness.Recorder.Received.Any(n => n.MarkerId == marker)
            && (await harness.GetOutboxRowsAsync()).Count == 0);

        await Assert.That(dispatched).IsTrue();
        await Assert.That(harness.Recorder.Received.Single(n => n.MarkerId == marker).Text).IsEqualTo("hello");
    }

    [Test]
    public async Task Enqueue_InsideRolledBackTransaction_ShouldNeverDispatch()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o => o.SweepInterval = SweepDisabled);
        await harness.StartDispatcherAsync();
        var marker = Guid.NewGuid();

        using (var scope = harness.NewScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<OutboxTestDbContext>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<OutboxTestDbContext>>();

            await using var transaction = await unitOfWork.BeginTransactionAsync();
            await outbox.EnqueueAsync(new OutboxTestNotification(marker, "doomed"));
            await unitOfWork.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await Task.Delay(400); // give a wrongly-signaled dispatch time to surface

        await Assert.That(harness.Recorder.Received).IsEmpty();
        await Assert.That(await harness.GetOutboxRowsAsync()).IsEmpty();
    }

    [Test]
    public async Task Enqueue_InsideCommittedTransaction_ShouldDispatchAfterCommit()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o => o.SweepInterval = SweepDisabled);
        await harness.StartDispatcherAsync();
        var marker = Guid.NewGuid();

        using (var scope = harness.NewScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<OutboxTestDbContext>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<OutboxTestDbContext>>();

            await using var transaction = await unitOfWork.BeginTransactionAsync();
            await outbox.EnqueueAsync(new OutboxTestNotification(marker, "committed"));
            await unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var dispatched = await OutboxTestHarness.PollUntilAsync(async () =>
            harness.Recorder.Received.Any(n => n.MarkerId == marker)
            && (await harness.GetOutboxRowsAsync()).Count == 0);

        await Assert.That(dispatched).IsTrue();
    }

    [Test]
    public async Task FailingHandler_OnHotPath_ShouldRecordAttemptAndError()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o => o.SweepInterval = SweepDisabled);
        await harness.StartDispatcherAsync();

        await EnqueueAndSaveAsync(harness, new AlwaysFailingNotification(Guid.NewGuid()));

        var failed = await OutboxTestHarness.PollUntilAsync(async () =>
        {
            if (harness.Recorder.FailingAttempts == 0) return false;
            var rows = await harness.GetOutboxRowsAsync();
            return rows.Count == 1 && rows[0].Attempts == 1 && rows[0].Error is not null;
        });

        await Assert.That(failed).IsTrue();
        var row = (await harness.GetOutboxRowsAsync()).Single();
        await Assert.That(row.Attempts).IsEqualTo(1);
        await Assert.That(row.NextAttemptAt).IsNotNull();
        await Assert.That(row.Error!).Contains("InvalidOperationException");
    }

    // ---------- Stored type names (no dispatcher: the row stays inspectable) ----------

    [Test]
    public async Task Enqueue_WithoutMapping_ShouldStoreFullTypeName()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o => o.SweepInterval = SweepDisabled);

        await EnqueueAndSaveAsync(harness, new OutboxTestNotification(Guid.NewGuid(), "raw"));

        var row = (await harness.GetOutboxRowsAsync()).Single();
        await Assert.That(row.Type).IsEqualTo(typeof(OutboxTestNotification).FullName!);
        await Assert.That(row.Payload).Contains("raw");
        await Assert.That(row.Attempts).IsEqualTo(0);
        await Assert.That(row.NextAttemptAt).IsNull();
    }

    [Test]
    public async Task Enqueue_WithMappedName_ShouldStoreMappedName()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o =>
        {
            o.SweepInterval = SweepDisabled;
            o.MapNotification<OutboxTestNotification>("outbox-test-mapped");
        });

        await EnqueueAndSaveAsync(harness, new OutboxTestNotification(Guid.NewGuid(), "mapped"));

        var row = (await harness.GetOutboxRowsAsync()).Single();
        await Assert.That(row.Type).IsEqualTo("outbox-test-mapped");
    }

    // ---------- Recovery sweep ----------

    [Test]
    public async Task OrphanedRow_ShouldBeSweptAndDispatched()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o =>
        {
            o.SweepInterval = TimeSpan.FromMilliseconds(100);
            o.GracePeriod = TimeSpan.Zero;
        });
        var marker = Guid.NewGuid();
        await harness.InsertRowDirectlyAsync(RowFor(new OutboxTestNotification(marker, "swept")));

        await harness.StartDispatcherAsync();

        var recovered = await OutboxTestHarness.PollUntilAsync(async () =>
            harness.Recorder.Received.Any(n => n.MarkerId == marker)
            && (await harness.GetOutboxRowsAsync()).Count == 0);

        await Assert.That(recovered).IsTrue();
    }

    [Test]
    public async Task OrphanedRow_WithMappedStoredName_ShouldResolveViaMapAndDispatch()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o =>
        {
            o.SweepInterval = TimeSpan.FromMilliseconds(100);
            o.GracePeriod = TimeSpan.Zero;
            o.MapNotification<OutboxTestNotification>("outbox-test-mapped");
        });
        var marker = Guid.NewGuid();
        var row = RowFor(new OutboxTestNotification(marker, "mapped-swept"));
        row.Type = "outbox-test-mapped";
        await harness.InsertRowDirectlyAsync(row);

        await harness.StartDispatcherAsync();

        var recovered = await OutboxTestHarness.PollUntilAsync(async () =>
            harness.Recorder.Received.Any(n => n.MarkerId == marker)
            && (await harness.GetOutboxRowsAsync()).Count == 0);

        await Assert.That(recovered).IsTrue();
    }

    [Test]
    public async Task DeadLetterRow_ShouldNeverBeDispatched()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o =>
        {
            o.SweepInterval = TimeSpan.FromMilliseconds(100);
            o.GracePeriod = TimeSpan.Zero;
            o.MaxAttempts = 2;
        });
        var dead = RowFor(
            new OutboxTestNotification(Guid.NewGuid(), "dead"),
            attempts: 2,
            nextAttemptAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
        await harness.InsertRowDirectlyAsync(dead);

        await harness.StartDispatcherAsync();
        await Task.Delay(600); // several sweep cycles

        await Assert.That(harness.Recorder.Received).IsEmpty();
        var row = (await harness.GetOutboxRowsAsync()).Single();
        await Assert.That(row.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task UnresolvableStoredType_ShouldRemainWithDescriptiveError()
    {
        await using var harness = await OutboxTestHarness.CreateAsync(o =>
        {
            o.SweepInterval = TimeSpan.FromMilliseconds(100);
            o.GracePeriod = TimeSpan.Zero;
        });
        var row = RowFor(new OutboxTestNotification(Guid.NewGuid(), "lost"));
        row.Type = "No.Such.Type";
        await harness.InsertRowDirectlyAsync(row);

        await harness.StartDispatcherAsync();

        var errored = await OutboxTestHarness.PollUntilAsync(async () =>
            (await harness.GetOutboxRowsAsync()).SingleOrDefault()?.Error is not null);

        await Assert.That(errored).IsTrue();
        var remaining = (await harness.GetOutboxRowsAsync()).Single();
        await Assert.That(remaining.Error!).Contains("No.Such.Type");
        await Assert.That(remaining.Error!).Contains("MapNotification");
        await Assert.That(harness.Recorder.Received).IsEmpty();
    }
}
