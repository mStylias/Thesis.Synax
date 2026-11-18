namespace Synax.Mediator.UnitTests.TestFixtures;

public record BookingCreatedNotification(Guid BookingId) : INotification;

public class BookingCreatedEmailHandler(HandlerSpy state)
    : INotificationHandler<BookingCreatedNotification>
{
    public async Task HandleAsync(BookingCreatedNotification notification, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
        state.ExecutedHandlers.Enqueue(nameof(BookingCreatedEmailHandler));
    }
}

public class BookingCreatedLogHandler(HandlerSpy state)
    : INotificationHandler<BookingCreatedNotification>
{
    public async Task HandleAsync(BookingCreatedNotification notification, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
        state.ExecutedHandlers.Enqueue(nameof(BookingCreatedLogHandler));
    }
}

public record UnregisteredNotification : INotification;
