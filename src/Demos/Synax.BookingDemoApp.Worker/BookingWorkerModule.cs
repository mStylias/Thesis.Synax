using Synax.BookingDemoApp.Application;
using Synax.BookingDemoApp.Infrastructure;
using Synax.Modules;

namespace Synax.BookingDemoApp.Worker;

/// <summary>
/// The worker host's module. It declares the same two shared modules the API does and adds the
/// background work of its own — no reference to the API project, and no endpoints, OpenAPI or JWT
/// coming along for the ride.
/// <para>
/// Depending on <c>BookingApplicationModule</c> also brings the outbox: the dispatcher hosted
/// service runs here as well as in the API, which is the point of the outbox being multi-instance
/// safe (the recovery sweep claims rows optimistically).
/// </para>
/// </summary>
public sealed class BookingWorkerModule : ISynaxModule
{
    public void Configure(ISynaxModuleBuilder builder)
    {
        builder.DependsOn<BookingApplicationModule>();
        builder.DependsOn<BookingInfrastructureModule>();

        builder.AddServices(services => services.AddHostedService<BookingMaintenanceService>());
    }
}
