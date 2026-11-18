using Microsoft.Extensions.DependencyInjection;

namespace Synax.Mediator.Internal;

/// <summary>
/// Type-erased base for notification pipelines (mirrors <see cref="RequestPipeline{TResponse}"/> pattern).
/// </summary>
internal abstract class NotificationPipeline
{
    public abstract Task ExecuteAsync(
        INotification notification,
        IServiceProvider serviceProvider,
        NotificationPublishStrategy strategy,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves and invokes all <see cref="INotificationHandler{TNotification}"/> instances
/// using the specified publish strategy.
/// </summary>
internal sealed class NotificationPipeline<TNotification> : NotificationPipeline
    where TNotification : INotification
{
    public override Task ExecuteAsync(
        INotification notification,
        IServiceProvider serviceProvider,
        NotificationPublishStrategy strategy,
        CancellationToken cancellationToken)
    {
        var handlers = serviceProvider.GetServices<INotificationHandler<TNotification>>();
        var typedNotification = (TNotification)notification;

        return strategy switch
        {
            NotificationPublishStrategy.Sequential => PublishSequential(handlers, typedNotification, cancellationToken),
            NotificationPublishStrategy.Parallel => PublishParallel(handlers, typedNotification, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null)
        };
    }

    private static async Task PublishSequential(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
            await handler.HandleAsync(notification, cancellationToken);
    }

    private static Task PublishParallel(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
    {
        var tasks = handlers.Select(h => h.HandleAsync(notification, cancellationToken));
        return Task.WhenAll(tasks);
    }
}
