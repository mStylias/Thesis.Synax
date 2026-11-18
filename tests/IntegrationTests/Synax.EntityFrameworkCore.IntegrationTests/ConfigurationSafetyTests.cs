using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Ownership;
using Synax.EntityFrameworkCore.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Model-level tests (no database) for the configuration safety net: the soft-delete filter
/// convention applies filters automatically, and the ownership validation convention turns a
/// missing ownership filter into a startup error instead of a silent tenant-data leak.
/// </summary>
public class ConfigurationSafetyTests
{
    private const string SoftDeleteFilterName = "SynaxSoftDelete";
    private const string OwnershipFilterName = "SynaxOwnership";

    public class ConventionWidget : IEntity, ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class ConventionOwnedWidget : IEntity, IHasOwner<int>
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
    }

    public sealed class StaticOwnerProvider : IOwnerProvider<int>
    {
        public object? GetCurrentOwnerId() => 1;
    }

    /// <summary>Soft delete configured via the single options call; OnModelCreating applies nothing.</summary>
    private sealed class SoftDeleteOnlyContext(DbContextOptions<SoftDeleteOnlyContext> options) : DbContext(options)
    {
        public DbSet<ConventionWidget> Widgets => Set<ConventionWidget>();
    }

    /// <summary>Ownership interceptor configured, but the ownership filter is (wrongly) missing.</summary>
    private sealed class OwnershipWithoutFilterContext(DbContextOptions<OwnershipWithoutFilterContext> options)
        : DbContext(options)
    {
        public DbSet<ConventionOwnedWidget> Widgets => Set<ConventionOwnedWidget>();
    }

    /// <summary>Ownership interceptor + filter, correctly configured.</summary>
    private sealed class OwnershipWithFilterContext(DbContextOptions<OwnershipWithFilterContext> options)
        : DbContext(options)
    {
        private readonly StaticOwnerProvider _ownerProvider = new();

        public DbSet<ConventionOwnedWidget> Widgets => Set<ConventionOwnedWidget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyOwnershipQueryFilters<int>(() => _ownerProvider.GetCurrentOwnerId());
    }

    /// <summary>Soft delete configured via options AND manually in OnModelCreating (must not clash).</summary>
    private sealed class DoubleConfiguredSoftDeleteContext(DbContextOptions<DoubleConfiguredSoftDeleteContext> options)
        : DbContext(options)
    {
        public DbSet<ConventionWidget> Widgets => Set<ConventionWidget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplySoftDeleteQueryFilters();
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        Action<DbContextOptionsBuilder<TContext>> configure)
        where TContext : DbContext
    {
        // Model building never opens the connection, so a placeholder connection string is fine.
        var builder = new DbContextOptionsBuilder<TContext>().UseNpgsql("Host=model-only");
        configure(builder);
        return builder.Options;
    }

    [Test]
    public async Task AddSoftDeleteInterceptor_AppliesQueryFilter_WithoutOnModelCreatingCall()
    {
        var options = CreateOptions<SoftDeleteOnlyContext>(b => b.AddSoftDeleteInterceptor());
        await using var context = new SoftDeleteOnlyContext(options);

        var entityType = context.Model.FindEntityType(typeof(ConventionWidget))!;

        await Assert.That(entityType.FindDeclaredQueryFilter(SoftDeleteFilterName)).IsNotNull();
    }

    [Test]
    public async Task AddSoftDeleteInterceptor_WithManualFilterCall_DoesNotClash()
    {
        var options = CreateOptions<DoubleConfiguredSoftDeleteContext>(b => b.AddSoftDeleteInterceptor());
        await using var context = new DoubleConfiguredSoftDeleteContext(options);

        var entityType = context.Model.FindEntityType(typeof(ConventionWidget))!;

        await Assert.That(entityType.FindDeclaredQueryFilter(SoftDeleteFilterName)).IsNotNull();
        await Assert.That(entityType.GetDeclaredQueryFilters().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task AddOwnershipInterceptor_WithoutQueryFilters_FailsModelBuilding()
    {
        var options = CreateOptions<OwnershipWithoutFilterContext>(b => b.AddOwnershipInterceptor<int>());
        await using var context = new OwnershipWithoutFilterContext(options);

        await Assert.That(() => context.Model)
            .Throws<InvalidOperationException>()
            .WithMessageContaining("tenant-data leak");
    }

    [Test]
    public async Task AddOwnershipInterceptor_WithQueryFilters_BuildsModel()
    {
        var options = CreateOptions<OwnershipWithFilterContext>(b => b.AddOwnershipInterceptor(new StaticOwnerProvider()));
        await using var context = new OwnershipWithFilterContext(options);

        var entityType = context.Model.FindEntityType(typeof(ConventionOwnedWidget))!;

        await Assert.That(entityType.FindDeclaredQueryFilter(OwnershipFilterName)).IsNotNull();
    }
}
