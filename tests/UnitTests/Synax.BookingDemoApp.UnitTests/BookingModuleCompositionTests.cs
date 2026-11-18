using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synax.AspNetCore.Endpoints;
using Synax.BookingDemoApp.Application;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Infrastructure;
using Synax.BookingDemoApp.Worker;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;
using Synax.Modules;
using Synax.Results;
using Synax.AspNetCore.Errors;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// The application and infrastructure modules are host-agnostic: the API and the worker each
/// declare their own module over the same two, and neither host references the other. These cover
/// that split, which is what stops a second host from having to reference the web project or
/// duplicate the declaration.
/// </summary>
public class BookingModuleCompositionTests
{
    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BookingDb"] = "Host=localhost;Database=booking;Username=demo;Password=demo"
            })
            .Build();

    private static IServiceCollection Register(params Assembly[] assemblies)
    {
        var services = new ServiceCollection();
        services.AddSynaxModules(BuildConfiguration(), assemblies);
        return services;
    }

    [Test]
    public async Task SharedModules_WithNoHostModule_ShouldValidateAndRegisterTheLayers()
    {
        // Arrange & Act — exactly what a host that wants the layers and nothing else gets
        var services = Register(
            typeof(BookingApplicationModule).Assembly,
            typeof(BookingInfrastructureModule).Assembly);

        // Assert — persistence and handlers are there...
        await Assert.That(services.Any(d => d.ServiceType == typeof(IUnitOfWork<BookingContext>))).IsTrue();
        await Assert.That(services.Any(d =>
            d.ServiceType == typeof(IRequestHandler<ArchiveOldBookingsRequest, Result<int, HttpError>>))).IsTrue();

        // ...and nothing HTTP came with them
        await Assert.That(services.Any(d => d.ServiceType == typeof(IEndpoint))).IsFalse();
    }

    [Test]
    public async Task WorkerModule_ShouldPullInBothSharedModulesWithoutTheApi()
    {
        // Act — the worker host's single call
        var services = Register(typeof(BookingWorkerModule).Assembly);

        // Assert — the shared layers arrived via DependsOn
        await Assert.That(services.Any(d => d.ServiceType == typeof(IUnitOfWork<BookingContext>))).IsTrue();
        await Assert.That(services.Any(d => d.ServiceType == typeof(IRepository<Domain.Entities.Booking, BookingContext>)))
            .IsTrue();

        // Assert — the worker's own background work, and no endpoints
        var hostedServices = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToArray();

        await Assert.That(hostedServices).Contains(typeof(BookingMaintenanceService));
        await Assert.That(services.Any(d => d.ServiceType == typeof(IEndpoint))).IsFalse();
    }

    [Test]
    public async Task WorkerModule_ShouldRunTheOutboxDispatcher()
    {
        // Arrange & Act — the outbox comes from the application module, so both hosts dispatch
        var services = Register(typeof(BookingWorkerModule).Assembly);

        // Assert
        var dispatchers = services.Count(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType?.Name.StartsWith("OutboxDispatcher") == true);

        await Assert.That(dispatchers).IsEqualTo(1);
    }

    [Test]
    public async Task BothHostModules_RegisteredTogether_ShouldConfigureTheSharedLayersOnce()
    {
        // Arrange & Act — a single process hosting both, the worst case for double registration
        var services = Register(
            typeof(BookingApiModule).Assembly,
            typeof(BookingWorkerModule).Assembly);

        // Assert — the shared modules are configured once no matter how many hosts depend on them
        await Assert.That(services.Count(d => d.ServiceType == typeof(IUnitOfWork<BookingContext>))).IsEqualTo(1);
        await Assert.That(services.Count(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType?.Name.StartsWith("OutboxDispatcher") == true)).IsEqualTo(1);
        await Assert.That(services.Any(d => d.ServiceType == typeof(IEndpoint))).IsTrue();
    }
}
