using System.Collections.Concurrent;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Everything the OData query parser needs for one exposed element type: its EDM model and — when
/// a capability requires an OData binder (today only <c>$search</c>) — the OData sub-container the
/// parser resolves that binder from. <see cref="Services"/> is <c>null</c> when no binder is
/// needed, which keeps the default path allocation-free and byte-identical to not having one.
/// </summary>
internal sealed record ODataElementContext(IEdmModel Model, IServiceProvider? Services);

/// <summary>
/// Per-<see cref="SynaxODataOptions"/> cache of <see cref="ODataElementContext"/> instances.
/// Lives on the options instance (rather than in a static dictionary) so container lifetime follows
/// the options object: a DI-registered singleton caches for the process, a per-request options
/// object is collected with the request instead of leaking a service provider per call.
/// </summary>
internal sealed class ODataElementContextCache(SynaxODataOptions options)
{
    /// <summary>
    /// Route prefix for the throwaway <see cref="ODataOptions"/> used to mint a sub-container.
    /// One <see cref="ODataOptions"/> is built per element type, so a constant prefix is enough.
    /// </summary>
    private const string RoutePrefix = "synax";

    private readonly ConcurrentDictionary<Type, ODataElementContext> _cache = new();

    public ODataElementContext GetOrAdd(Type elementType) => _cache.GetOrAdd(elementType, Build);

    private ODataElementContext Build(Type elementType)
    {
        var model = EdmModelCache.GetOrAdd(elementType, options.ConfigureModel);

        // Only $search needs a binder, and only via the OData sub-container: ODataQueryContext's
        // RequestContainer setter is internal, so the container is the single lever we have.
        // Assigning a plain IServiceProvider is not an option — the parser resolves SkipTokenHandler
        // and friends from it and fails if they are missing.
        if (!options.HasSearchableMembers)
            return new ODataElementContext(model, Services: null);

        try
        {
            var odata = new ODataOptions();
            odata.AddRouteComponents(
                RoutePrefix,
                model,
                services => services.AddSingleton<ISearchBinder>(new SynaxSearchBinder(options)));

            return new ODataElementContext(model, odata.GetRouteServices(RoutePrefix));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to build the OData query services for '{elementType.FullName}'. This is " +
                "needed because searchable members are declared via SynaxODataOptions.Searchable<T>(...); " +
                "remove them to fall back to the model-only path.",
                ex);
        }
    }
}
