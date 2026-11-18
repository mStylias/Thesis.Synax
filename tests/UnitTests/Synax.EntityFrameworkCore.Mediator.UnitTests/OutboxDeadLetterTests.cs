using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator.UnitTests.TestFixtures.Outbox;
using Synax.Mediator;

namespace Synax.EntityFrameworkCore.Mediator.UnitTests;

/// <summary>
/// Permanent-failure (dead-letter) tests: a fast sweep runs in every test, so if a
/// dead-lettered row were still eligible for retry, the handler would be invoked again
/// within the observation window — the "no second attempt" asserts are load-bearing.
/// </summary>
public class OutboxDeadLetterTests
{
    private const int MaxAttempts = 3;

    private static Task<OutboxTestHarness> CreateHarnessAsync(Action<OutboxOptions>? extra = null)
        => OutboxTestHarness.CreateAsync(o =>
        {
            o.SweepInterval = TimeSpan.FromMilliseconds(100);
            o.GracePeriod = TimeSpan.Zero;
            o.MaxAttempts = MaxAttempts;
            extra?.Invoke(o);
        });

    private static async Task EnqueueAndSaveAsync(OutboxTestHarness harness, INotification notification)
    {
        using var scope = harness.NewScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<OutboxTestDbContext>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<OutboxTestDbContext>>();

        await outbox.EnqueueAsync(notification);
        await unitOfWork.SaveChangesAsync();
    }

    private static async Task AssertDeadLetteredWithoutRetryAsync(
        OutboxTestHarness harness, Guid marker, string expectedErrorFragment)
    {
        var deadLettered = await OutboxTestHarness.PollUntilAsync(async () =>
        {
            var rows = await harness.GetOutboxRowsAsync();
            return rows.Count == 1 && rows[0].Attempts == MaxAttempts && rows[0].Error is not null;
        });
        await Assert.That(deadLettered).IsTrue();

        // Several sweep cycles: a not-quite-dead row would be re-dispatched here.
        await Task.Delay(500);

        await Assert.That(harness.Recorder.AttemptsFor(marker)).IsEqualTo(1);
        var row = (await harness.GetOutboxRowsAsync()).Single();
        await Assert.That(row.Attempts).IsEqualTo(MaxAttempts);
        await Assert.That(row.Error!).Contains(expectedErrorFragment);
    }

    [Test]
    public async Task Handler_ThrowingOutboxDeadLetterException_ShouldDeadLetterWithoutRetry()
    {
        await using var harness = await CreateHarnessAsync();
        await harness.StartDispatcherAsync();
        var marker = Guid.NewGuid();

        await EnqueueAndSaveAsync(harness, new DeadLetterNotification(marker));

        await AssertDeadLetteredWithoutRetryAsync(harness, marker, "OutboxDeadLetterException");
    }

    [Test]
    public async Task Handler_ThrowingExceptionMatchedByDeadLetterWhen_ShouldDeadLetterWithoutRetry()
    {
        await using var harness = await CreateHarnessAsync(o =>
            o.DeadLetterWhen = ex => ex is TestPermanentException);
        await harness.StartDispatcherAsync();
        var marker = Guid.NewGuid();

        await EnqueueAndSaveAsync(harness, new PermanentByPredicateNotification(marker));

        await AssertDeadLetteredWithoutRetryAsync(harness, marker, "TestPermanentException");
    }

    [Test]
    public async Task Handler_WrappingDeadLetterExceptionAsInner_ShouldDeadLetterWithoutRetry()
    {
        await using var harness = await CreateHarnessAsync();
        await harness.StartDispatcherAsync();
        var marker = Guid.NewGuid();

        await EnqueueAndSaveAsync(harness, new WrappedDeadLetterNotification(marker));

        // The recorded error describes the outermost exception; the inner one triggered death.
        await AssertDeadLetteredWithoutRetryAsync(harness, marker, "InvalidOperationException");
    }

    [Test]
    public async Task Handler_ThrowingUnmatchedException_ShouldFollowNormalRetryPath()
    {
        await using var harness = await CreateHarnessAsync(o =>
        {
            o.SweepInterval = TimeSpan.FromHours(1); // isolate the hot path
            o.DeadLetterWhen = ex => ex is TestPermanentException;
        });
        await harness.StartDispatcherAsync();

        // AlwaysFailingNotification throws InvalidOperationException — not matched by the predicate.
        await EnqueueAndSaveAsync(harness, new AlwaysFailingNotification(Guid.NewGuid()));

        var retriedNormally = await OutboxTestHarness.PollUntilAsync(async () =>
        {
            var rows = await harness.GetOutboxRowsAsync();
            return rows.Count == 1 && rows[0].Attempts == 1 && rows[0].Error is not null;
        });

        await Assert.That(retriedNormally).IsTrue();
        var row = (await harness.GetOutboxRowsAsync()).Single();
        await Assert.That(row.Attempts).IsEqualTo(1);          // not MaxAttempts — still retryable
        await Assert.That(row.NextAttemptAt).IsNotNull();      // retry scheduled
    }
}
