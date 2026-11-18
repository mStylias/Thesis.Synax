using Microsoft.EntityFrameworkCore;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Mediator;
using Synax.EntityFrameworkCore.Ownership;

namespace Synax.BookingDemoApp.Infrastructure;

public class BookingDbContext(
    DbContextOptions<BookingDbContext> options,
    IOwnerProvider<Guid> ownerProvider)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);

        // Tenant scoping: the accessor must reference a DbContext member (the primary-ctor
        // parameter) so EF re-evaluates the current owner per query instead of baking a constant
        // into cached SQL. Startup validation (from AddOwnershipInterceptor) fails the model if an
        // owned entity is ever missed by this call.
        // The second accessor is what makes userContext.BecomeAllOwners() work: it adds a
        // cross-owner term to each filter, so admin/reporting code can read across tenants
        // without looping owner-by-owner. Writes are unaffected — there is still no ambient
        // owner to stamp inside such a scope.
        modelBuilder.ApplyOwnershipQueryFilters<Guid>(
            () => ownerProvider.GetCurrentOwnerId(),
            () => ownerProvider.IsCrossOwner());

        // The transactional outbox table ("SynaxOutbox"); rows are staged by IOutbox.EnqueueAsync
        // and committed atomically with business changes.
        modelBuilder.AddSynaxOutbox<BookingContext>();

        // Soft-delete query filters are applied automatically by AddSoftDeleteInterceptor().
    }
}
