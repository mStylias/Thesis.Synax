using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Synax.Tests.Common;

public abstract class DatabaseFixture<TDbContext> : IAsyncInitializer, IAsyncDisposable
    where TDbContext : DbContext
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    private ServiceProvider? _serviceProvider;

    public string ConnectionString => _container.GetConnectionString();

    public IServiceProvider Services =>
        _serviceProvider ?? throw new InvalidOperationException("Fixture has not been initialized.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<TDbContext>(options =>
        {
            options.UseNpgsql(ConnectionString);
            ConfigureDbContext(options);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    protected virtual void ConfigureDbContext(DbContextOptionsBuilder options)
    {
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public AsyncServiceScope CreateScope() =>
        (_serviceProvider ?? throw new InvalidOperationException("Fixture has not been initialized."))
        .CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();

        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
