using Synax.AspNetCore.HealthChecks;

namespace Synax.AspNetCore.UnitTests.TestFixtures;

/// <summary>Health check that counts how many times it has been executed.</summary>
public class CountingCheck : ISynaxHealthCheck
{
    private int _count;

    public string Name => "Counting Service";

    public int Count => Volatile.Read(ref _count);

    public Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return Task.FromResult(SynaxHealthCheckResult.Healthy(Name));
    }
}

/// <summary>A TimeProvider whose timestamp only moves when advanced manually.</summary>
public class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Volatile.Read(ref _ticks);

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _ticks, delta.Ticks);
}
