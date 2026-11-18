using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

public class SendTests
{
    private static (IMediator Mediator, HandlerSpy Spy) CreateMediator()
    {
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSynaxMediator(typeof(HandlerSpy).Assembly);

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(),
                scope.ServiceProvider.GetRequiredService<HandlerSpy>());
    }

    [Test]
    public async Task SendAsync_WhenCommandReturnsResponse_ShouldExecuteHandlerAndReturnResult()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        var id = await mediator.SendAsync(new CreateBookingCommand("Hotel", 150m));

        // Assert
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(CreateBookingCommandHandler));
    }

    [Test]
    public async Task SendAsync_WhenVoidCommand_ShouldExecuteHandler()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(DeleteBookingCommandHandler));
    }

    [Test]
    public async Task SendAsync_WhenQuery_ShouldReturnResult()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();
        var bookingId = Guid.NewGuid();

        // Act
        var result = await mediator.SendAsync(new GetBookingQuery(bookingId));

        // Assert
        await Assert.That(result.Id).IsEqualTo(bookingId);
        await Assert.That(result.Name).IsEqualTo("Default Booking");
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(GetBookingQueryHandler));
    }

    [Test]
    public async Task SendAsync_WhenNoHandlerRegistered_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var (mediator, _) = CreateMediator();

        // Act & Assert
        await Assert.That(() => mediator.SendAsync(new UnregisteredCommand()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_WhenNullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        var (mediator, _) = CreateMediator();

        // Act & Assert
        await Assert.That(() => mediator.SendAsync<Unit>(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SendAsync_WhenTokenProvided_ShouldPropagateToHandler()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();
        using var cts = new CancellationTokenSource();

        // Act
        await mediator.SendAsync(new CreateBookingCommand("Test", 10m), cts.Token);

        // Assert
        await Assert.That(spy.LastReceivedToken).IsEqualTo(cts.Token);
    }

    [Test]
    public async Task SendAsync_WhenTokenAlreadyCancelled_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var (mediator, _) = CreateMediator();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.That(() => mediator.SendAsync(new CreateBookingCommand("Test", 10m), cts.Token))
            .Throws<OperationCanceledException>();
    }
}
