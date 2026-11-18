using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Auditing;
using Synax.EntityFrameworkCore.Mapper;
using Synax.EntityFrameworkCore.Mediator;
using Synax.EntityFrameworkCore.Ownership;
using Synax.EntityFrameworkCore.SoftDelete;

namespace Synax.BookingDemoApp.Infrastructure;

/// <summary>
/// The infrastructure wiring Synax has no opinion about. Everything Synax does own — repositories,
/// unit of work, the outbox services — is declared by <c>BookingModule</c> instead, which is also
/// what calls this.
/// </summary>
public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Interceptors and handlers resolve the clock from DI — swap for a FakeTimeProvider in tests.
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<BookingDbContext>(options =>
            options
                .UseNpgsql(configuration.GetConnectionString("BookingDb"))
                // Each interceptor is a single registration; providers (user, owner, clock)
                // resolve from the application's DI per save.
                .AddAuditingInterceptor()          // CreatedAt/CreatedById on insert, UpdatedAt/ModifiedById on update
                .AddSoftDeleteInterceptor()        // delete conversion + automatic query filters
                .AddOwnershipInterceptor<Guid>()   // OwnerId stamping + startup filter validation
                .AddOutboxInterceptor<BookingContext, BookingDbContext>());  // signals the outbox dispatcher post-commit

        // Bridges the repositories' QueryDto/AddDtoAsync to Synax.Mapper, whose profiles the
        // module registers. The repository/unit-of-work registration itself is the module's
        // AddPersistence<BookingDbContext, BookingContext>() call.
        services.AddSynaxRepositoryMapper();

        return services;
    }
}
