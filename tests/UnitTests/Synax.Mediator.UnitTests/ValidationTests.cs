using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

public class ValidationTests
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
    public async Task SendAsync_WhenTypedValidationPasses_ShouldExecuteHandler()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new ProcessOrderCommand("ORD-1", 50m));

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.OrderId).IsEqualTo("ORD-1");
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(ProcessOrderHandler));
    }

    [Test]
    public async Task SendAsync_WhenTypedValidationFails_ShouldReturnResponseWithoutExecutingHandler()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new ProcessOrderCommand("ORD-2", -10m));

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Message).IsEqualTo("Amount must be positive");
        await Assert.That(spy.ExecutedHandlers.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenValidationDeclaredAgainstBaseRequestTypeAndPasses_ShouldExecuteHandler()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new RefundOrderCommand("ORD-3", 25m));

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.OrderId).IsEqualTo("ORD-3");
        await Assert.That(spy.ExecutedHandlers).Contains(nameof(RefundOrderHandler));
    }

    [Test]
    public async Task SendAsync_WhenValidationDeclaredAgainstBaseRequestTypeAndFails_ShouldShortCircuit()
    {
        // Arrange
        var (mediator, spy) = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new RefundOrderCommand("ORD-4", -5m));

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Message).IsEqualTo("Refund amount must be positive");
        await Assert.That(spy.ExecutedHandlers.Count).IsEqualTo(0);
    }
}
