using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

public class PipelineBehaviorTests
{
    private static (IMediator Mediator, HandlerSpy Spy) CreateMediator(
        Action<MediatorOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSynaxMediator(opts =>
        {
            opts.AddHandlersFromAssemblyContaining<HandlerSpy>();
            configure?.Invoke(opts);
        });

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(),
                scope.ServiceProvider.GetRequiredService<HandlerSpy>());
    }

    [Test]
    public async Task SendAsync_WhenNoBehaviors_ShouldExecuteHandlerDirectly()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(1);
        await Assert.That(handlers[0]).IsEqualTo(nameof(DeleteBookingCommandHandler));
    }

    [Test]
    public async Task SendAsync_WhenGlobalBehaviorRegistered_ShouldExecuteBeforeAndAfterHandler()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(opts =>
            opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)));

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(3);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo(nameof(DeleteBookingCommandHandler));
        await Assert.That(handlers[2]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenAttributeBehaviorApplied_ShouldExecuteBehavior()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        await mediator.SendAsync(new AuditedCommand("test"));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(3);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo(nameof(AuditedCommandHandler));
        await Assert.That(handlers[2]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenDirectMediatorBehaviorAttributeApplied_ShouldExecuteBehavior()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act — TimedCommandHandler is decorated with [MediatorBehavior(typeof(TimingBehavior<,>))] directly
        await mediator.SendAsync(new TimedCommand("test"));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(3);
        await Assert.That(handlers[0]).IsEqualTo("TimingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo(nameof(TimedCommandHandler));
        await Assert.That(handlers[2]).IsEqualTo("TimingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenSkipBehaviorApplied_ShouldNotExecuteSkippedGlobalBehavior()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(opts =>
            opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)));

        // Act
        await mediator.SendAsync(new SkippedCommand("test"));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(1);
        await Assert.That(handlers[0]).IsEqualTo(nameof(SkippedCommandHandler));
    }

    [Test]
    public async Task SendAsync_WhenMultipleGlobalBehaviors_ShouldExecuteInRegistrationOrder()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(opts =>
        {
            opts.AddGlobalBehavior(typeof(LoggingBehavior<,>));
            opts.AddGlobalBehavior(typeof(TimingBehavior<,>));
        });

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(5);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo("TimingBehavior_Before");
        await Assert.That(handlers[2]).IsEqualTo(nameof(DeleteBookingCommandHandler));
        await Assert.That(handlers[3]).IsEqualTo("TimingBehavior_After");
        await Assert.That(handlers[4]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenGlobalAndAttributeBehaviors_ShouldRunGlobalsBeforeAttributes()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(opts =>
            opts.AddGlobalBehavior(typeof(TimingBehavior<,>)));

        // Act — AuditedCommand handler has [Log] attribute
        await mediator.SendAsync(new AuditedCommand("test"));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(5);
        await Assert.That(handlers[0]).IsEqualTo("TimingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[2]).IsEqualTo(nameof(AuditedCommandHandler));
        await Assert.That(handlers[3]).IsEqualTo("LoggingBehavior_After");
        await Assert.That(handlers[4]).IsEqualTo("TimingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenBehaviorWithValidation_ShouldRunBehaviorAroundValidation()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(opts =>
            opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)));

        // Act
        await mediator.SendAsync(new ProcessOrderCommand("ORD-1", 50m));

        // Assert — behavior wraps validation + handler
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(3);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo(nameof(ProcessOrderHandler));
        await Assert.That(handlers[2]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenScopedGlobalBehaviorMatchesRequestType_ShouldExecuteBehavior()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(opts =>
            opts.AddGlobalBehavior(
                typeof(LoggingBehavior<,>),
                appliesTo: requestType => requestType == typeof(DeleteBookingCommand)));

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(3);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo(nameof(DeleteBookingCommandHandler));
        await Assert.That(handlers[2]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenScopedGlobalBehaviorDoesNotMatchRequestType_ShouldNotExecuteBehavior()
    {
        // Arrange — the behavior is scoped to a different request type
        var (mediator, spy) = CreateMediator(opts =>
            opts.AddGlobalBehavior(
                typeof(LoggingBehavior<,>),
                appliesTo: requestType => requestType == typeof(CreateBookingCommand)));

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert — only the handler ran, the scoped behavior did not attach
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(1);
        await Assert.That(handlers[0]).IsEqualTo(nameof(DeleteBookingCommandHandler));
    }

    [Test]
    public async Task SendAsync_WhenScopedGlobalBehaviorMatchesButHandlerSkipsIt_ShouldNotExecuteBehavior()
    {
        // Arrange — the predicate matches, but SkippedCommandHandler carries [SkipBehavior(typeof(LoggingBehavior<,>))]
        var (mediator, spy) = CreateMediator(opts =>
            opts.AddGlobalBehavior(
                typeof(LoggingBehavior<,>),
                appliesTo: requestType => requestType == typeof(SkippedCommand)));

        // Act
        await mediator.SendAsync(new SkippedCommand("test"));

        // Assert — [SkipBehavior] still wins over the matching scope
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(1);
        await Assert.That(handlers[0]).IsEqualTo(nameof(SkippedCommandHandler));
    }
}
