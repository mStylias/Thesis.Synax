namespace Synax.AspNetCore.HealthChecks;

/// <summary>
/// Implement this interface to provide a custom health check.
/// Register via <c>services.AddSynaxHealthChecks(opts => opts.AddCheck&lt;MyCheck&gt;())</c>.
/// </summary>
/// <example>
/// <code>
/// public class DatabaseHealthCheck : ISynaxHealthCheck
/// {
///     public string Name => "Database";
///     public async Task&lt;SynaxHealthCheckResult&gt; CheckAsync(CancellationToken ct)
///     {
///         var ok = await db.CanConnectAsync(ct);
///         return ok
///             ? SynaxHealthCheckResult.Healthy(Name)
///             : SynaxHealthCheckResult.Unhealthy(Name, "Cannot connect");
///     }
/// }
/// </code>
/// </example>
public interface ISynaxHealthCheck
{
    /// <summary>
    /// A human-readable name for this health check.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the health check and returns the result.
    /// </summary>
    Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
