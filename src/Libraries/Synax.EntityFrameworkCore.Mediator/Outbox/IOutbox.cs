using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Transactional outbox: stages a notification to be published reliably <em>after</em> the
/// current unit of work commits. The event row is saved by the same <c>SaveChangesAsync</c>
/// as the business change (atomic); on commit an in-process channel hands it straight to the
/// dispatcher — the database is only read back for retries and crash recovery.
/// <para>
/// Delivery is at-least-once: handlers must be idempotent. Dispatch goes through
/// <see cref="IPublisher"/>, so an outboxed event's handler is a plain notification handler.
/// </para>
/// </summary>
public interface IOutbox<TContext> where TContext : ISynaxContext
{
    /// <summary>
    /// Stages <paramref name="notification"/> in the outbox. Nothing is durable until the
    /// unit of work saves; nothing is dispatched until the enclosing transaction (if any) commits.
    /// </summary>
    Task EnqueueAsync(INotification notification, CancellationToken cancellationToken = default);
}
