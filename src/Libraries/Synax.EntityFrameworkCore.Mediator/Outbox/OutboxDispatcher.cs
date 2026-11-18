using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Dispatches outbox events. Two loops:
/// <list type="bullet">
///   <item><b>Hot path</b> — reads the in-process channel (row id + materialized notification,
///   zero database reads), publishes via <see cref="IPublisher"/>, deletes the row on success.</item>
///   <item><b>Recovery sweep</b> — periodically claims rows that are due for retry or were
///   orphaned (lost signal, crash, another instance's death). Claiming is optimistic
///   (<c>WHERE Attempts = seen</c>), so multiple instances never double-dispatch a row.</item>
/// </list>
/// Delivery is at-least-once; rows that exhaust <see cref="OutboxOptions.MaxAttempts"/> stay
/// in the table as dead letters with their last error.
/// </summary>
internal sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    OutboxChannel<TContext> channel,
    OutboxOptionsHolder<TContext> optionsHolder,
    ILogger<OutboxDispatcher<TContext>> logger,
    TimeProvider? timeProvider = null) : BackgroundService
    where TContext : ISynaxContext
{
    private readonly OutboxOptions _options = optionsHolder.Value;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(PumpChannelAsync(stoppingToken), SweepLoopAsync(stoppingToken));

    // ---------- Hot path ----------

    private async Task PumpChannelAsync(CancellationToken ct)
    {
        await foreach (var item in channel.ReadAllAsync(ct))
        {
            var failure = await PublishAndDeleteAsync(item.Id, item.Notification, ct);
            if (failure is null) continue;

            if (IsPermanent(failure))
                await DeadLetterAsync(item.Id, failure, ct);
            else
                await MarkFirstAttemptFailedAsync(item.Id, failure, ct);
        }
    }

    // ---------- Recovery sweep ----------

    private async Task SweepLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.SweepInterval, _time);

        while (await WaitForNextTickSafeAsync(timer, ct))
        {
            try
            {
                await SweepOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox sweep cycle failed; retrying next interval");
            }
        }
    }

    private static async Task<bool> WaitForNextTickSafeAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var orphanCutoff = now - _options.GracePeriod;
        var maxAttempts = _options.MaxAttempts;
        var batchSize = _options.SweepBatchSize;

        List<OutboxMessage<TContext>> due;
        using (var scope = scopeFactory.CreateScope())
        {
            due = await GetRepository(scope)
                .Query(q => q
                    .Where(m => m.Attempts < maxAttempts)
                    .Where(m => m.NextAttemptAt != null
                        ? m.NextAttemptAt <= now
                        : m.CreatedAt <= orphanCutoff)
                    .OrderBy(m => m.CreatedAt)
                    .Take(batchSize))
                .ToListAsync(ct);
        }

        foreach (var row in due)
        {
            ct.ThrowIfCancellationRequested();

            if (!await TryClaimAsync(row, ct))
                continue; // another instance got it

            INotification notification;
            try
            {
                var type = _options.ResolveType(row.Type);
                notification = (INotification)(JsonSerializer.Deserialize(row.Payload, type)
                    ?? throw new InvalidOperationException("Payload deserialized to null."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox message {MessageId} of type '{StoredType}' could not be materialized",
                    row.Id, row.Type);
                await SetErrorAsync(row.Id, ex, ct);
                continue;
            }

            var failure = await PublishAndDeleteAsync(row.Id, notification, ct);
            if (failure is null) continue;

            if (IsPermanent(failure))
                await DeadLetterAsync(row.Id, failure, ct);
            else
                await SetErrorAsync(row.Id, failure, ct);
        }
    }

    /// <summary>Optimistic claim: exactly one instance wins the attempt increment. The bumped
    /// NextAttemptAt doubles as a lease — if this attempt crashes, the row comes due again
    /// after the backoff.</summary>
    private async Task<bool> TryClaimAsync(OutboxMessage<TContext> row, CancellationToken ct)
    {
        var seenAttempts = row.Attempts;
        var nextAttemptAt = _time.GetUtcNow() + _options.Backoff(seenAttempts + 1);

        using var scope = scopeFactory.CreateScope();
        var claimed = await GetRepository(scope).ExecuteUpdateAsync(
            q => q.Where(m => m.Id == row.Id && m.Attempts == seenAttempts),
            s => s
                .SetProperty(m => m.Attempts, seenAttempts + 1)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt),
            ct);

        return claimed == 1;
    }

    // ---------- Shared dispatch ----------

    /// <summary>Publishes and deletes the row; returns <c>null</c> on success, the failure otherwise.</summary>
    private async Task<Exception?> PublishAndDeleteAsync(Guid id, INotification notification, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPublisher>().PublishAsync(notification, ct);

            await GetRepository(scope).ExecuteHardDeleteAsync(q => q.Where(m => m.Id == id), ct);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox dispatch of message {MessageId} failed", id);
            return ex;
        }
    }

    /// <summary>Hot-path failure: the row has never been attempted, so Attempts moves 0 → 1
    /// and the retry is scheduled without reading the row back.</summary>
    private async Task MarkFirstAttemptFailedAsync(Guid id, Exception failure, CancellationToken ct)
    {
        var nextAttemptAt = _time.GetUtcNow() + _options.Backoff(1);
        var error = Describe(failure);

        try
        {
            using var scope = scopeFactory.CreateScope();
            await GetRepository(scope).ExecuteUpdateAsync(
                q => q.Where(m => m.Id == id),
                s => s
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                    .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                    .SetProperty(m => m.Error, error),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The row stays due via the grace period; the sweep will retry it.
            logger.LogError(ex, "Failed to record outbox failure for message {MessageId}", id);
        }
    }

    /// <summary>Sweep-path failure: the claim already bumped Attempts/NextAttemptAt; only the error is recorded.</summary>
    private async Task SetErrorAsync(Guid id, Exception failure, CancellationToken ct)
    {
        var error = Describe(failure);

        try
        {
            using var scope = scopeFactory.CreateScope();
            await GetRepository(scope).ExecuteUpdateAsync(
                q => q.Where(m => m.Id == id),
                s => s.SetProperty(m => m.Error, error),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to record outbox error for message {MessageId}", id);
        }
    }

    /// <summary>Permanent failure: mark the row dead (sweep skips <c>Attempts &gt;= MaxAttempts</c>)
    /// and keep it, with its error, for inspection and manual requeue.</summary>
    private async Task DeadLetterAsync(Guid id, Exception failure, CancellationToken ct)
    {
        var maxAttempts = _options.MaxAttempts;
        var error = Describe(failure);
        logger.LogWarning("Outbox message {MessageId} dead-lettered without retry: {Error}", id, error);

        try
        {
            using var scope = scopeFactory.CreateScope();
            await GetRepository(scope).ExecuteUpdateAsync(
                q => q.Where(m => m.Id == id),
                s => s
                    .SetProperty(m => m.Attempts, maxAttempts)
                    .SetProperty(m => m.Error, error),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to dead-letter outbox message {MessageId}", id);
        }
    }

    /// <summary>An <see cref="OutboxDeadLetterException"/> anywhere in the chain (including
    /// aggregates from parallel publish) or a match on <see cref="OutboxOptions.DeadLetterWhen"/>
    /// makes the failure permanent.</summary>
    private bool IsPermanent(Exception failure)
    {
        foreach (var exception in Unwrap(failure))
        {
            if (exception is OutboxDeadLetterException) return true;
            if (_options.DeadLetterWhen?.Invoke(exception) == true) return true;
        }

        return false;
    }

    private static IEnumerable<Exception> Unwrap(Exception root)
    {
        var stack = new Stack<Exception>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions) stack.Push(inner);
            else if (current.InnerException is { } inner)
                stack.Push(inner);
        }
    }

    private static string Describe(Exception exception)
        => $"{exception.GetType().Name}: {exception.Message}";

    private static IRepository<OutboxMessage<TContext>, TContext> GetRepository(IServiceScope scope)
        => scope.ServiceProvider
            .GetRequiredService<IUnitOfWork<TContext>>()
            .GetRepository<OutboxMessage<TContext>>();
}
