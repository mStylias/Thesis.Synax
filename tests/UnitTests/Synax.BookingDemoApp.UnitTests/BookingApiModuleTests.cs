using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.HealthChecks;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mapper.Core;
using Synax.Modules;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// The demo's whole Synax surface goes through one <c>AddSynaxModules</c> call, and that call
/// verifies it. Registration touches no database, so this runs as a unit test — and it fails if
/// someone adds an endpoint, handler, profile, entity or health check that
/// <see cref="BookingApiModule"/> or the two modules it depends on do not cover.
/// </summary>
public class BookingApiModuleTests
{
    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BookingDb"] = "Host=localhost;Database=booking;Username=demo;Password=demo"
            })
            .Build();

    private static IServiceCollection RegisterModule()
    {
        var services = new ServiceCollection();
        services.AddSynaxModules(BuildConfiguration(), typeof(BookingApiModule).Assembly);
        return services;
    }

    [Test]
    public async Task AddSynaxModules_ShouldRegisterTheDemoSurfaceWithNoGaps()
    {
        // Arrange
        var act = () => RegisterModule();

        // Act & Assert — surface validation runs inside the call
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task AddSynaxModules_ShouldRegisterEveryEndpointInTheWebAssembly()
    {
        // Arrange
        var declared = typeof(BookingApiModule).Assembly
            .GetTypes()
            .Count(type => type is { IsClass: true, IsAbstract: false } && typeof(IEndpoint).IsAssignableFrom(type));

        // Act
        var services = RegisterModule();

        // Assert
        await Assert.That(declared).IsGreaterThan(0);
        await Assert.That(services.Count(d => d.ServiceType == typeof(IEndpoint))).IsEqualTo(declared);
    }

    [Test]
    public async Task AddSynaxModules_ShouldRegisterPersistenceUnderTheContextTag()
    {
        // Act
        var services = RegisterModule();

        // Assert — the application layer resolves against the tag, never the DbContext
        await Assert.That(services.Any(d => d.ServiceType == typeof(IUnitOfWork<BookingContext>))).IsTrue();
        await Assert.That(services.Any(d => d.ServiceType == typeof(IRepository<Booking, BookingContext>))).IsTrue();
        await Assert.That(services.Any(d => d.ServiceType == typeof(IRepository<Restaurant, BookingContext>))).IsTrue();
    }

    [Test]
    public async Task AddSynaxModules_ShouldRegisterTheHealthChecksFromInfrastructure()
    {
        // Act
        var services = RegisterModule();

        // Assert
        await Assert.That(services.Count(d => d.ServiceType == typeof(ISynaxHealthCheck))).IsEqualTo(2);
    }

    [Test]
    public async Task AddSynaxModules_ShouldProduceAValidMapperConfiguration()
    {
        // Arrange — the same assertion Program.cs makes at startup in Development
        var services = RegisterModule();
        var provider = services.BuildServiceProvider();

        // Act
        var act = () => provider.GetRequiredService<MapperConfiguration>().AssertConfigurationIsValid();

        // Assert
        await Assert.That(act).ThrowsNothing();
    }
}
