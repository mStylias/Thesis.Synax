using Synax.EntityFrameworkCore.Abstractions.Core;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// A pending outbox event, persisted in the same transaction as the business change that
/// produced it. Rows are deleted on successful dispatch, so the table's contents are the
/// work queue: pending, retrying, or dead (<c>Attempts &gt;= MaxAttempts</c>, kept with
/// <see cref="Error"/> for inspection and manual requeue).
/// </summary>
public sealed class OutboxMessage<TContext> : IEntity where TContext : ISynaxContext
{
    public Guid Id { get; set; }

    /// <summary>The stored notification type name (see <see cref="OutboxOptions.MapNotification{T}"/>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The JSON-serialized notification.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Dispatch attempts so far (a sweep claim counts as an attempt).</summary>
    public int Attempts { get; set; }

    /// <summary>When the next retry is due; <c>null</c> until the first attempt fails.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>The last dispatch error, if any.</summary>
    public string? Error { get; set; }
}
