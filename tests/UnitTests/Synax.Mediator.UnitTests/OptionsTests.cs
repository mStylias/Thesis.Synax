using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

public class OptionsTests
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
    public async Task SendAsync_WhenOptionsProvided_ShouldUseProvidedOptions()
    {
        // Arrange
        var (mediator, _) = CreateMediator();
        var preloaded = new BookingDto(Guid.NewGuid(), "Preloaded", 42m);

        // Act
        var result = await mediator.SendAsync(
            new GetBookingQuery(preloaded.Id) { Options = new() { PreloadedBooking = preloaded } });

        // Assert
        await Assert.That(result.Name).IsEqualTo("Preloaded");
        await Assert.That(result.Price).IsEqualTo(42m);
    }

    [Test]
    public async Task SendAsync_WhenOptionsNotSet_ShouldUseDefaults()
    {
        // Arrange
        var (mediator, _) = CreateMediator();
        var bookingId = Guid.NewGuid();

        // Act
        var result = await mediator.SendAsync(new GetBookingQuery(bookingId));

        // Assert
        await Assert.That(result.Id).IsEqualTo(bookingId);
        await Assert.That(result.Name).IsEqualTo("Default Booking");
    }

    [Test]
    public async Task SendAsync_WhenSequentialCallsWithOptions_ShouldIsolateOptionsPerCall()
    {
        // Arrange
        var (mediator, _) = CreateMediator();
        var preloaded = new BookingDto(Guid.NewGuid(), "WithOptions", 1m);

        // Act
        var withPreloaded = await mediator.SendAsync(
            new GetBookingQuery(preloaded.Id) { Options = new() { PreloadedBooking = preloaded } });
        var withDefaults = await mediator.SendAsync(new GetBookingQuery(Guid.NewGuid()));

        // Assert
        await Assert.That(withPreloaded.Name).IsEqualTo("WithOptions");
        await Assert.That(withDefaults.Name).IsEqualTo("Default Booking");
    }
}
