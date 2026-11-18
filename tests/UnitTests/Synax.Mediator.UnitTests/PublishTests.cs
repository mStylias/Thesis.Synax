using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

public class PublishTests
{
    private static (IMediator Mediator, HandlerSpy Spy) CreateMediator(
        NotificationPublishStrategy strategy = NotificationPublishStrategy.Sequential)
    {
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSynaxMediator(opts =>
        {
            opts.AddHandlersFromAssemblyContaining<HandlerSpy>();
            opts.NotificationPublishStrategy = strategy;
        });

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(),
                scope.ServiceProvider.GetRequiredService<HandlerSpy>());
    }

    [Test]
    public async Task PublishAsync_WhenMultipleHandlersRegistered_ShouldNotifyAll()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        await mediator.PublishAsync(new BookingCreatedNotification(Guid.NewGuid()));

        // Assert
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedEmailHandler));
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedLogHandler));
    }

    [Test]
    public async Task PublishAsync_WhenNoHandlersRegistered_ShouldCompleteWithoutError()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        await mediator.PublishAsync(new UnregisteredNotification());

        // Assert
        await Assert.That(spy.ExecutedHandlers.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PublishAsync_WhenParallelStrategy_ShouldExecuteAllHandlers()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(NotificationPublishStrategy.Parallel);

        // Act
        await mediator.PublishAsync(new BookingCreatedNotification(Guid.NewGuid()));

        // Assert
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedEmailHandler));
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedLogHandler));
    }

    [Test]
    public async Task PublishAsync_WhenDefaultIsSequential_ShouldAllowParallelOverride()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(NotificationPublishStrategy.Sequential);

        // Act
        await mediator.PublishAsync(
            new BookingCreatedNotification(Guid.NewGuid()),
            NotificationPublishStrategy.Parallel);

        // Assert
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedEmailHandler));
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedLogHandler));
    }

    [Test]
    public async Task PublishAsync_WhenDefaultIsParallel_ShouldAllowSequentialOverride()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(NotificationPublishStrategy.Parallel);

        // Act
        await mediator.PublishAsync(
            new BookingCreatedNotification(Guid.NewGuid()),
            NotificationPublishStrategy.Sequential);

        // Assert
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedEmailHandler));
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedLogHandler));
    }
}
