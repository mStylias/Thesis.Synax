namespace Synax.Mediator;

/// <summary>
/// Defines a handler for a notification of type <typeparamref name="TNotification"/>.
/// </summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles the notification.
    /// </summary>
    Task HandleAsync(TNotification notification, CancellationToken cancellationToken);
}
