namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// A <see cref="TimeProvider"/> returning a fixed instant, so tests can assert exact timestamps.
/// </summary>
public sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
