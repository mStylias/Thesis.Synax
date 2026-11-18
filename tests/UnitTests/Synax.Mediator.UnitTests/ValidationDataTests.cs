using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.Internal;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

public class ValidationDataTests
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
    public async Task SendAsync_WhenDataCarryingValidationPasses_ShouldFlowStateToHandler()
    {
        var (mediator, spy) = CreateMediator();

        var result = await mediator.SendAsync(new ShipOrderCommand("ORD-9"));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.OrderId).IsEqualTo("validated:ORD-9");
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(ShipOrderHandler));
    }

    [Test]
    public async Task SendAsync_WhenDataCarryingValidationFails_ShouldShortCircuitWithoutExecutingHandler()
    {
        var (mediator, spy) = CreateMediator();

        var result = await mediator.SendAsync(new ShipOrderCommand(""));

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Message).IsEqualTo("Order id is required");
        await Assert.That(spy.ExecutedHandlers.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RegisterHandlerType_WhenValidationTErrorDoesNotMatchResponse_ShouldThrowAtStartup()
    {
        var services = new ServiceCollection();
        var registry = new HandlerRegistry();
        var registrar = new HandlerRegistrar(services, ServiceLifetime.Scoped, registry);

        await Assert.That(() => registrar.RegisterHandlerType(typeof(MismatchedValidationHandler<object>)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("must match");
    }

    [Test]
    public async Task ValidationData_WhenValidationHasNotRun_ShouldThrowMentioningValidateAndHandleAsync()
    {
        var handler = new ShipOrderHandler(new HandlerSpy());

        await Assert.That(() => handler.ValidationData())
            .Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(WithValidationExtensions.ValidateAndHandleAsync));
    }

    // -------- ValidateAndHandleAsync (direct invocation, e.g. unit tests) --------

    [Test]
    public async Task ValidateAndHandleAsync_WhenValidationPasses_ShouldPopulateValidationDataAndReturnHandlerResult()
    {
        var spy = new HandlerSpy();
        var handler = new ShipOrderHandler(spy);

        var result = await handler.ValidateAndHandleAsync(new ShipOrderCommand("ORD-5"));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.OrderId).IsEqualTo("validated:ORD-5");
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(ShipOrderHandler));
    }

    [Test]
    public async Task ValidateAndHandleAsync_WhenValidationFails_ShouldReturnErrorWithoutInvokingHandler()
    {
        var spy = new HandlerSpy();
        var handler = new ShipOrderHandler(spy);

        var result = await handler.ValidateAndHandleAsync(new ShipOrderCommand(""));

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Message).IsEqualTo("Order id is required");
        await Assert.That(spy.ExecutedHandlers.Count).IsEqualTo(0);
    }
}
