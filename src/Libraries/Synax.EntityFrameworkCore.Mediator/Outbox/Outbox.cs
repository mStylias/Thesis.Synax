using System.Text.Json;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

internal sealed class Outbox<TContext>(
    IUnitOfWork<TContext> unitOfWork,
    OutboxPendingBuffer<TContext> buffer,
    OutboxOptionsHolder<TContext> options,
    TimeProvider? timeProvider = null) : IOutbox<TContext>
    where TContext : ISynaxContext
{
    public async Task EnqueueAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var notificationType = notification.GetType();
        var message = new OutboxMessage<TContext>
        {
            Id = Guid.CreateVersion7(),
            Type = options.Value.GetStoredName(notificationType),
            Payload = JsonSerializer.Serialize(notification, notificationType),
            CreatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow()
        };

        await unitOfWork.GetRepository<OutboxMessage<TContext>>().AddAsync(message, cancellationToken);
        buffer.Add(message.Id, notification);
    }
}
