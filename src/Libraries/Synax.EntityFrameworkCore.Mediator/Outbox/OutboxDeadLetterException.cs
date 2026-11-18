// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Thrown by a notification handler to signal that an outbox dispatch failure is permanent —
/// the message is dead-lettered immediately (kept in the table with its error, skipped by the
/// sweep) instead of being retried. For exception types you don't control, use
/// <see cref="OutboxOptions.DeadLetterWhen"/> instead.
/// </summary>
public sealed class OutboxDeadLetterException : Exception
{
    public OutboxDeadLetterException(string message) : base(message)
    {
    }

    public OutboxDeadLetterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
