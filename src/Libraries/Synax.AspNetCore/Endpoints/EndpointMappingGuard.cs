using Microsoft.Extensions.Hosting;

namespace Synax.AspNetCore.Endpoints;

/// <summary>
/// Records whether <c>MapSynaxEndpoints</c> ran. Registered only by the module system, which is
/// the context in which "endpoints exist but nothing routed them" is a wiring mistake rather than a
/// legitimate choice.
/// </summary>
internal sealed class EndpointMappingProbe
{
    private int _mapped;

    /// <summary>Set when a module registered at least one endpoint, so mapping is expected.</summary>
    public bool Expected { get; set; }

    public bool Mapped => Volatile.Read(ref _mapped) == 1;

    public void MarkMapped() => Interlocked.Exchange(ref _mapped, 1);
}

/// <summary>
/// Fails host startup when endpoints were registered but never mapped — the silent-404 failure
/// mode, moved from "requests mysteriously 404" to an error before the server accepts traffic.
/// </summary>
internal sealed class EndpointMappingGuard(EndpointMappingProbe probe) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!probe.Expected || probe.Mapped)
            return Task.CompletedTask;

        throw new InvalidOperationException(
            "Synax endpoints were registered by a module but app.MapSynaxEndpoints() was never called, " +
            "so none of them are routed. Call app.MapSynaxEndpoints() on the built WebApplication.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
