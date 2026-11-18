using Synax.AspNetCore.HealthChecks;

namespace Synax.AspNetCore.UnitTests.TestFixtures;

public class HealthyCheck : ISynaxHealthCheck
{
    public string Name => "Healthy Service";

    public Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SynaxHealthCheckResult.Healthy(Name, "All good"));
}

public class UnhealthyCheck : ISynaxHealthCheck
{
    public string Name => "Unhealthy Service";

    public Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SynaxHealthCheckResult.Unhealthy(Name, "Connection refused"));
}

public class DegradedCheck : ISynaxHealthCheck
{
    public string Name => "Degraded Service";

    public Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SynaxHealthCheckResult.Degraded(Name, "High latency"));
}

public class ThrowingCheck : ISynaxHealthCheck
{
    public string Name => "Throwing Service";

    public Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Check failed");
}
