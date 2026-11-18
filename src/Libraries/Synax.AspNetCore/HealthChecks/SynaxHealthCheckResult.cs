namespace Synax.AspNetCore.HealthChecks;

/// <summary>
/// The result of a single health check execution.
/// </summary>
public sealed class SynaxHealthCheckResult
{
    public required string Name { get; init; }
    public required SynaxHealthStatus Status { get; init; }
    public string? Description { get; init; }
    public TimeSpan Duration { get; init; }

    public static SynaxHealthCheckResult Healthy(string name, string? description = null)
        => new() { Name = name, Status = SynaxHealthStatus.Healthy, Description = description };

    public static SynaxHealthCheckResult Degraded(string name, string? description = null)
        => new() { Name = name, Status = SynaxHealthStatus.Degraded, Description = description };

    public static SynaxHealthCheckResult Unhealthy(string name, string? description = null)
        => new() { Name = name, Status = SynaxHealthStatus.Unhealthy, Description = description };
}
