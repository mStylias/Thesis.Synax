using System.Threading.Channels;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>A committed outbox row plus its already-materialized notification — the hot path
/// dispatches without ever reading the database.</summary>
internal readonly record struct OutboxItem(Guid Id, INotification Notification);

/// <summary>
/// The in-process hot-path channel between commit and dispatcher. Bounded with drop-on-full:
/// the database row is the source of truth, so a dropped signal only means the recovery sweep
/// delivers that event instead — backpressure can never block a save.
/// </summary>
internal sealed class OutboxChannel<TContext> where TContext : ISynaxContext
{
    private readonly Channel<OutboxItem> _channel;

    public OutboxChannel(OutboxOptionsHolder<TContext> options)
    {
        _channel = Channel.CreateBounded<OutboxItem>(new BoundedChannelOptions(options.Value.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });
    }

    public void TryWrite(OutboxItem item) => _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<OutboxItem> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>Per-context wrapper so several DbContexts can each have their own outbox options.</summary>
internal sealed record OutboxOptionsHolder<TContext>(OutboxOptions Value) where TContext : ISynaxContext;
