using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.Internal;
using Synax.Mediator.UnitTests.TestFixtures;
using Synax.Results;

namespace Synax.Mediator.UnitTests;

public class RegistrationTests
{
    private static (IMediator Mediator, HandlerSpy Spy) BuildMediator(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(),
                scope.ServiceProvider.GetRequiredService<HandlerSpy>());
    }

    [Test]
    public async Task PublishAsync_WhenSameAssemblyRegisteredTwice_ShouldInvokeEachHandlerOnce()
    {
        // Arrange — two AddHandlersFromAssemblyContaining calls resolving to the same assembly
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSynaxMediator(opts =>
        {
            opts.AddHandlersFromAssemblyContaining<HandlerSpy>();
            opts.AddHandlersFromAssemblyContaining<RegistrationTests>();
        });
        var (mediator, spy) = BuildMediator(services);

        // Act
        await mediator.PublishAsync(new BookingCreatedNotification(Guid.NewGuid()));

        // Assert — email + log handler, once each
        await Assert.That(spy.ExecutedHandlers.Count).IsEqualTo(2);
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedEmailHandler));
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(BookingCreatedLogHandler));
    }

    [Test]
    public async Task AddSynaxMediator_WhenCalledTwice_ShouldKeepFirstCallHandlersReachable()
    {
        // Arrange — a second call (e.g. from another module) must merge, not shadow, the first registry
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSynaxMediator(typeof(HandlerSpy).Assembly);
        services.AddSynaxMediator(typeof(Result).Assembly);
        var (mediator, spy) = BuildMediator(services);

        // Act
        var id = await mediator.SendAsync(new CreateBookingCommand("Hotel", 150m));

        // Assert
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(CreateBookingCommandHandler));
    }

    [Test]
    public async Task AddSynaxMediator_WhenCalledTwiceWithSameAssembly_ShouldInvokeNotificationHandlersOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSynaxMediator(typeof(HandlerSpy).Assembly);
        services.AddSynaxMediator(typeof(HandlerSpy).Assembly);
        var (mediator, spy) = BuildMediator(services);

        // Act
        await mediator.PublishAsync(new BookingCreatedNotification(Guid.NewGuid()));

        // Assert
        await Assert.That(spy.ExecutedHandlers.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RegisterHandlerType_WhenSecondHandlerForSameRequest_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var registry = new HandlerRegistry();
        var registrar = new HandlerRegistrar(services, ServiceLifetime.Scoped, registry);
        registrar.RegisterHandlerType(typeof(DuplicateHandlerA<object>));

        // Act & Assert
        await Assert.That(() => registrar.RegisterHandlerType(typeof(DuplicateHandlerB<object>)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(ConflictedCommand));
    }
}
