using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;
using Synax.EntityFrameworkCore.Ownership;
using Synax.EntityFrameworkCore.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests;

public class TestDbContext(
    DbContextOptions<TestDbContext> options,
    IOwnerProvider<int> ownerProvider) : DbContext(options), ISynaxContext
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<SoftDeletableBooking> SoftDeletableBookings => Set<SoftDeletableBooking>();
    public DbSet<AuditedSoftDeletableBooking> AuditedSoftDeletableBookings => Set<AuditedSoftDeletableBooking>();
    public DbSet<TestUser> TestUsers => Set<TestUser>();
    public DbSet<OwnedBooking> OwnedBookings => Set<OwnedBooking>();
    public DbSet<OptionallyOwnedBooking> OptionallyOwnedBookings => Set<OptionallyOwnedBooking>();
    public DbSet<OwnedSoftDeletableBooking> OwnedSoftDeletableBookings => Set<OwnedSoftDeletableBooking>();
    public DbSet<TypedBooking> TypedBookings => Set<TypedBooking>();
    public DbSet<ConcurrentSoftDeleteBooking> ConcurrentSoftDeleteBookings => Set<ConcurrentSoftDeleteBooking>();
    public DbSet<GuidKeyedBooking> GuidKeyedBookings => Set<GuidKeyedBooking>();
    public DbSet<AuditedBooking> AuditedBookings => Set<AuditedBooking>();
    public DbSet<AuditTimesSoftDeleteBooking> AuditTimesSoftDeleteBookings => Set<AuditTimesSoftDeleteBooking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TypedBooking>().Property(b => b.StatusText).HasConversion<string>();
        modelBuilder.Entity<TypedBooking>().Property(b => b.Price).HasPrecision(18, 2);

        modelBuilder.ApplyOwnershipQueryFilters<int>(
            () => ownerProvider.GetCurrentOwnerId(),
            () => ownerProvider.IsCrossOwner());
        modelBuilder.ApplySoftDeleteQueryFilters();
    }
}
