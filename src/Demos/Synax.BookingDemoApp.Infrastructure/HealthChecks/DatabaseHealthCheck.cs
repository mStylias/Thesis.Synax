using Synax.AspNetCore.HealthChecks;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.BookingDemoApp.Infrastructure.HealthChecks;

public class DatabaseHealthCheck(IDatabaseManager<BookingContext> databaseManager) : ISynaxHealthCheck
{
    public string Name => "PostgreSQL";

    public async Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var canConnect = await databaseManager.CanConnectAsync(cancellationToken);

        return canConnect
            ? SynaxHealthCheckResult.Healthy(Name, $"Provider: {databaseManager.ProviderName}")
            : SynaxHealthCheckResult.Unhealthy(Name, "Cannot connect to the database.");
    }
}
