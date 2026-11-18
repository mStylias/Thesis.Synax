using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Registers the shared <see cref="SynaxODataOptions"/> used by the OData query helpers.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers a shared <see cref="SynaxODataOptions"/> instance that the request-based OData
    /// helpers resolve when no explicit settings are passed. Optional — without it the helpers use
    /// the built-in defaults. Uses <c>TryAdd</c>: an already registered instance wins.
    /// </summary>
    public static IServiceCollection AddSynaxOData(
        this IServiceCollection services,
        Action<SynaxODataOptions>? configure = null)
    {
        var options = new SynaxODataOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        return services;
    }
}
