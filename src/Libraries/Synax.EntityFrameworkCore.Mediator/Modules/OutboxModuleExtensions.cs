using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Modules;

namespace Synax.EntityFrameworkCore.Mediator.Modules;

/// <summary>
/// The outbox's contribution to <see cref="ISynaxModuleBuilder"/>, covering the DI half of its
/// registration.
/// </summary>
/// <remarks>
/// The other two touch points stay where they are: <c>AddOutboxInterceptor</c> belongs to the
/// <c>DbContext</c>'s options and <c>modelBuilder.AddSynaxOutbox</c> to <c>OnModelCreating</c> —
/// neither is reachable from a service collection, and the model half owns a migration the app
/// must control. Surface validation cannot see those, so a module that enqueues to an outbox
/// without a mapped table still fails at dispatch rather than at startup.
/// </remarks>
public static class OutboxModuleExtensions
{
    /// <summary>
    /// Registers the transactional outbox for <typeparamref name="TContext"/>: the enqueue API,
    /// the in-process channel, and the dispatcher hosted service.
    /// </summary>
    public static ISynaxModuleBuilder AddOutbox<TContext>(
        this ISynaxModuleBuilder builder, Action<OutboxOptions>? configure = null)
        where TContext : ISynaxContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services => services.AddSynaxOutbox<TContext>(configure));
    }
}
