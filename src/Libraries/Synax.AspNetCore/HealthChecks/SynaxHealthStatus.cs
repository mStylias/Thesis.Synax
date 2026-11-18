namespace Synax.AspNetCore.HealthChecks;

/// <summary>
/// Represents the health status of a component.
/// </summary>
public enum SynaxHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}
