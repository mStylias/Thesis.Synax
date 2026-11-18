using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.Mapper;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Auditing;
using Synax.EntityFrameworkCore.Mapper;
using Synax.EntityFrameworkCore.Ownership;
using Synax.EntityFrameworkCore.SoftDelete;
using Synax.Tests.Common;

namespace Synax.EntityFrameworkCore.IntegrationTests;

public class BookingDatabaseFixture : DatabaseFixture<TestDbContext>
{
    public TestSoftDeleteUserProvider UserProvider { get; } = new();
    public TestAuditUserProvider AuditUserProvider { get; } = new();
    public TestOwnerProvider OwnerProvider { get; } = new();
    public TestTimeProvider TimeProvider { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    protected override void ConfigureDbContext(DbContextOptionsBuilder options)
    {
        // Parameterless on purpose: the ISoftDeleteUserProvider, IOwnerProvider<int>, and
        // IAuditUserProvider are registered in DI below, exercising the interceptors'
        // service-provider fallback.
        options.AddSoftDeleteInterceptor();
        options.AddOwnershipInterceptor<int>();
        options.AddAuditingInterceptor();
        options.AddInterceptors(QueryCompilationCounter.Instance);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        var assembly = typeof(BookingDatabaseFixture).Assembly;
        services.AddSynaxEfCore(options =>
        {
            options
                .ForDbContext<TestDbContext>()
                .ScanNamespace(assembly, "Synax.EntityFrameworkCore.IntegrationTests.TestModels");
        });
        services.AddSynaxMapper(assembly);
        services.AddSynaxRepositoryMapper();
        services.AddSingleton<ISoftDeleteUserProvider>(UserProvider);
        services.AddSingleton<IAuditUserProvider>(AuditUserProvider);
        services.AddSingleton<IOwnerProvider<int>>(OwnerProvider);
        services.AddSingleton<TimeProvider>(TimeProvider);
    }
}
