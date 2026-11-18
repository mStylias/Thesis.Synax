namespace Synax.AspNetCore.HealthChecks;

/// <summary>
/// Aggregate response returned by the health check endpoint.
/// </summary>
public sealed class SynaxHealthCheckResponse
{
    public required SynaxHealthStatus OverallStatus { get; init; }
    public required IReadOnlyList<SynaxHealthCheckResult> Checks { get; init; }
    public required TimeSpan TotalDuration { get; init; }
}
