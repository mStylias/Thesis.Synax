using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

/// <summary>
/// Global behaviors belong to the application, not to the <c>AddSynaxMediator</c> call that
/// registered them. A modular app wires each module with its own call, so a behavior and the
/// handlers it must wrap routinely arrive in different calls — in either order.
/// </summary>
public class ModularRegistrationTests
{
    /// <summary>Applies the given calls in order, then resolves the mediator.</summary>
    private static (IMediator Mediator, HandlerSpy Spy) CreateMediator(
        params Action<MediatorOptions>[] calls)
    {
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();

        foreach (var call in calls)
            services.AddSynaxMediator(call);

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(),
                scope.ServiceProvider.GetRequiredService<HandlerSpy>());
    }

    private static void ScanHandlers(MediatorOptions opts)
        => opts.AddHandlersFromAssemblyContaining<HandlerSpy>();

    // -------- Behaviors reach handlers registered by another call --------

    [Test]
    public async Task SendAsync_WhenBehaviorRegisteredByLaterCall_ShouldWrapEarlierCallHandlers()
    {
        // Arrange — module registers its handlers, the host adds a behavior afterwards
        var (mediator, spy) = CreateMediator(
            ScanHandlers,
            opts => opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)));

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
    public async Task SendAsync_WhenBehaviorRegisteredByEarlierCall_ShouldWrapLaterCallHandlers()
    {
        // Arrange — the host declares its cross-cutting behavior before any module is added
        var (mediator, spy) = CreateMediator(
            opts => opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)),
            ScanHandlers);

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
    public async Task SendAsync_WhenBehaviorsSplitAcrossCalls_ShouldPreserveRegistrationOrder()
    {
        // Arrange
        var (mediator, spy) = CreateMediator(
            opts => opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)),
            ScanHandlers,
            opts => opts.AddGlobalBehavior(typeof(TimingBehavior<,>)));

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert — order follows registration across calls, outermost first
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(5);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo("TimingBehavior_Before");
        await Assert.That(handlers[2]).IsEqualTo(nameof(DeleteBookingCommandHandler));
        await Assert.That(handlers[3]).IsEqualTo("TimingBehavior_After");
        await Assert.That(handlers[4]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenBehaviorRegisteredByLaterCall_ShouldStillRespectSkipBehavior()
    {
        // Arrange — SkippedCommandHandler carries [SkipBehavior(typeof(LoggingBehavior<,>))]
        var (mediator, spy) = CreateMediator(
            ScanHandlers,
            opts => opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)));

        // Act
        await mediator.SendAsync(new SkippedCommand("test"));

        // Assert
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(1);
        await Assert.That(handlers[0]).IsEqualTo(nameof(SkippedCommandHandler));
    }

    // -------- Re-registering the same behavior widens, never duplicates --------

    [Test]
    public async Task SendAsync_WhenTwoCallsRegisterTheSameBehavior_ShouldRunItOnce()
    {
        // Arrange — two modules independently asking for the same cross-cutting behavior
        var (mediator, spy) = CreateMediator(
            opts =>
            {
                ScanHandlers(opts);
                opts.AddGlobalBehavior(typeof(LoggingBehavior<,>));
            },
            opts => opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)));

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert — wrapped once, not nested twice
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(3);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo(nameof(DeleteBookingCommandHandler));
        await Assert.That(handlers[2]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenSameBehaviorScopedByTwoCalls_ShouldApplyToBothScopes()
    {
        // Arrange — each module scopes the shared behavior to its own request type
        Action<MediatorOptions>[] calls =
        [
            ScanHandlers,
            opts => opts.AddGlobalBehavior(
                typeof(LoggingBehavior<,>), appliesTo: t => t == typeof(DeleteBookingCommand)),
            opts => opts.AddGlobalBehavior(
                typeof(LoggingBehavior<,>), appliesTo: t => t == typeof(CreateBookingCommand))
        ];

        var (deleteMediator, deleteSpy) = CreateMediator(calls);
        var (createMediator, createSpy) = CreateMediator(calls);

        // Act
        await deleteMediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));
        await createMediator.SendAsync(new CreateBookingCommand("Hotel", 150m));

        // Assert — the predicates are OR-ed, so both scopes are honoured
        await Assert.That(deleteSpy.ExecutedHandlers).Contains("LoggingBehavior_Before");
        await Assert.That(createSpy.ExecutedHandlers).Contains("LoggingBehavior_Before");
    }

    [Test]
    public async Task SendAsync_WhenScopedBehaviorAlsoRegisteredUnconditionally_ShouldApplyEverywhere()
    {
        // Arrange — a module scopes the behavior, the host then wants it on everything
        var (mediator, spy) = CreateMediator(
            ScanHandlers,
            opts => opts.AddGlobalBehavior(
                typeof(LoggingBehavior<,>), appliesTo: t => t == typeof(CreateBookingCommand)),
            opts => opts.AddGlobalBehavior(typeof(LoggingBehavior<,>)));

        // Act — a request outside the original scope
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert — the unconditional registration subsumes the predicate
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(3);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[2]).IsEqualTo("LoggingBehavior_After");
    }

    [Test]
    public async Task SendAsync_WhenBehaviorReRegistered_ShouldKeepItsFirstPipelinePosition()
    {
        // Arrange — Logging is registered first, then again after Timing
        var (mediator, spy) = CreateMediator(
            opts =>
            {
                ScanHandlers(opts);
                opts.AddGlobalBehavior(typeof(LoggingBehavior<,>));
            },
            opts =>
            {
                opts.AddGlobalBehavior(typeof(TimingBehavior<,>));
                opts.AddGlobalBehavior(typeof(LoggingBehavior<,>));
            });

        // Act
        await mediator.SendAsync(new DeleteBookingCommand(Guid.NewGuid()));

        // Assert — Logging keeps the outer slot it claimed first; order stays deterministic
        var handlers = spy.ExecutedHandlers.ToArray();
        await Assert.That(handlers).Count().IsEqualTo(5);
        await Assert.That(handlers[0]).IsEqualTo("LoggingBehavior_Before");
        await Assert.That(handlers[1]).IsEqualTo("TimingBehavior_Before");
        await Assert.That(handlers[3]).IsEqualTo("TimingBehavior_After");
        await Assert.That(handlers[4]).IsEqualTo("LoggingBehavior_After");
    }
}
