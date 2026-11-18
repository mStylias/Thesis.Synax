using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Modules;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.EntityFrameworkCore.AspNetCore;
using Synax.EntityFrameworkCore.Modules;
using Synax.Modules;

namespace Synax.BookingDemoApp.Infrastructure;

/// <summary>
/// The persistence surface: the entities' repositories under the <c>BookingContext</c> tag, the
/// health checks, and the <c>DbContext</c> itself.
/// <para>
/// Host-agnostic, and a sibling of <c>BookingApplicationModule</c> — neither references the other,
/// matching the project layout. Whichever host wants persistence depends on this.
/// </para>
/// </summary>
public sealed class BookingInfrastructureModule : ISynaxModule
{
    public void Configure(ISynaxModuleBuilder builder)
    {
        var configuration = builder.Configuration
            ?? throw new InvalidOperationException(
                "BookingInfrastructureModule needs configuration — pass it to AddSynaxModules.");

        // The entities live in the domain assembly; this module owns their persistence.
        builder.IncludeAssemblyOf<Booking>();

        // The one place the standalone tag and the concrete context meet: repositories, unit of
        // work and database manager for every IEntity in this module's assemblies.
        builder.AddPersistence<BookingDbContext, BookingContext>();

        builder.AddHealthChecks();

        // The interceptors need IAuditUserProvider / ISoftDeleteUserProvider / IOwnerProvider, and
        // BookingDbContext takes IOwnerProvider<Guid> in its constructor — so persistence declares
        // the bridge rather than leaving every host to remember it. A host that authenticates
        // (the API) re-registers it with its own claim configuration; Configure accumulates and
        // the services themselves are TryAdd, so both calls compose.
        builder.AddServices(services => services.AddSynaxEfCoreIdentityBridge<Guid>());

        // Everything Synax has no opinion about: the DbContext and its interceptors, the clock,
        // and the mapper bridge.
        builder.AddServices(services => services.AddInfrastructure(configuration));
    }
}
