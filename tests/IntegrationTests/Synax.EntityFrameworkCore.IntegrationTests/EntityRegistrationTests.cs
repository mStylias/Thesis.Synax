using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.EntityScanning;
using Synax.EntityFrameworkCore.IntegrationTests.EntityScanning.Child;
using Synax.EntityFrameworkCore.IntegrationTests.EntityScanningOther;
using Synax.EntityFrameworkCore.IntegrationTests.OtherEntityScanning;

namespace Synax.EntityFrameworkCore.IntegrationTests;

public class EntityRegistrationTests
{
    [Test]
    public async Task AddSynaxEfCore_WhenNamespaceIsConfigured_ShouldRegisterRootAndChildNamespaceEntities()
    {
        var services = new ServiceCollection();

        services.AddSynaxEfCore(options =>
        {
            options
                .ForDbContext<TestDbContext>()
                .ScanNamespace(typeof(EntityRegistrationTests).Assembly, "Synax.EntityFrameworkCore.IntegrationTests.EntityScanning");
        });

        await Assert.That(HasRepository<RootScannedEntity, TestDbContext>(services)).IsTrue();
        await Assert.That(HasRepository<ChildScannedEntity, TestDbContext>(services)).IsTrue();
    }

    [Test]
    public async Task AddSynaxEfCore_WhenNamespaceIsConfigured_ShouldNotRegisterSiblingNamespaceEntities()
    {
        var services = new ServiceCollection();

        services.AddSynaxEfCore(options =>
        {
            options
                .ForDbContext<TestDbContext>()
                .ScanNamespace(typeof(EntityRegistrationTests).Assembly, "Synax.EntityFrameworkCore.IntegrationTests.EntityScanning");
        });

        await Assert.That(HasRepository<OutsideScannedEntity, TestDbContext>(services)).IsFalse();
    }

    [Test]
    public async Task AddSynaxEfCore_WhenMultipleDbContextsAreConfigured_ShouldCorrelateEachNamespaceWithItsDbContext()
    {
        var services = new ServiceCollection();

        services.AddSynaxEfCore(options =>
        {
            options.RepositoryLifetime = ServiceLifetime.Singleton;
            options.UnitOfWorkLifetime = ServiceLifetime.Transient;
            options.DatabaseManagerLifetime = ServiceLifetime.Singleton;

            options
                .ForDbContext<TestDbContext>()
                .ScanNamespace(typeof(EntityRegistrationTests).Assembly, "Synax.EntityFrameworkCore.IntegrationTests.EntityScanning");

            options
                .ForDbContext<OtherDbContext>()
                .ScanNamespace(typeof(EntityRegistrationTests).Assembly, "Synax.EntityFrameworkCore.IntegrationTests.OtherEntityScanning");
        });

        await Assert.That(HasRepository<RootScannedEntity, TestDbContext>(services)).IsTrue();
        await Assert.That(HasRepository<OtherScannedEntity, OtherDbContext>(services)).IsTrue();
        await Assert.That(HasRepository<RootScannedEntity, OtherDbContext>(services)).IsFalse();
        await Assert.That(GetRepositoryLifetime<RootScannedEntity, TestDbContext>(services)).IsEqualTo(ServiceLifetime.Singleton);
        await Assert.That(GetUnitOfWorkLifetime<TestDbContext>(services)).IsEqualTo(ServiceLifetime.Transient);
    }

    private static bool HasRepository<TEntity, TDbContext>(IServiceCollection services)
        where TEntity : class, IEntity
        where TDbContext : DbContext, ISynaxContext
    {
        return services.Any(descriptor =>
            descriptor.ServiceType == typeof(IRepository<TEntity, TDbContext>));
    }

    private static ServiceLifetime? GetRepositoryLifetime<TEntity, TDbContext>(IServiceCollection services)
        where TEntity : class, IEntity
        where TDbContext : DbContext, ISynaxContext
    {
        return services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IRepository<TEntity, TDbContext>))?.Lifetime;
    }

    private static ServiceLifetime? GetUnitOfWorkLifetime<TDbContext>(IServiceCollection services)
        where TDbContext : DbContext, ISynaxContext
    {
        return services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IUnitOfWork<TDbContext>))?.Lifetime;
    }

    private sealed class OtherDbContext(DbContextOptions<OtherDbContext> options) : DbContext(options), ISynaxContext;
}
