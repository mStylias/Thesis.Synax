namespace Synax.Mediator;

/// <summary>
/// Publishes notifications to all registered handlers.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification using the default <see cref="NotificationPublishStrategy"/>.
    /// </summary>
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Publishes a notification using the specified <paramref name="strategy"/>.
    /// </summary>
    Task PublishAsync<TNotification>(TNotification notification, NotificationPublishStrategy strategy, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
