namespace Synax.AspNetCore.HealthChecks;

/// <summary>
/// Configuration for Synax health checks. Use <see cref="AddCheck{TCheck}"/>
/// to register health check implementations.
/// </summary>
public sealed class SynaxHealthCheckOptions
{
    internal List<Type> HealthCheckTypes { get; } = [];

    /// <summary>
    /// Registers a health check implementation.
    /// </summary>
    public SynaxHealthCheckOptions AddCheck<TCheck>() where TCheck : class, ISynaxHealthCheck
    {
        HealthCheckTypes.Add(typeof(TCheck));
        return this;
    }

    /// <summary>
    /// Registers a health check implementation by type.
    /// </summary>
    public SynaxHealthCheckOptions AddCheck(Type checkType)
    {
        if (!typeof(ISynaxHealthCheck).IsAssignableFrom(checkType))
            throw new ArgumentException(
                $"Type {checkType.Name} does not implement {nameof(ISynaxHealthCheck)}.", nameof(checkType));

        HealthCheckTypes.Add(checkType);
        return this;
    }
}
