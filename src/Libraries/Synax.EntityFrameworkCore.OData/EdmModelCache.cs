using System.Collections.Concurrent;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Caches EDM models per (element type, model customization) pair — building one is expensive and
/// the model never changes for a given type. Camel-cased so EDM names match the JSON clients see.
/// </summary>
internal static class EdmModelCache
{
    private static readonly ConcurrentDictionary<(Type ElementType, Action<ODataConventionModelBuilder>? Configure), IEdmModel>
        Cache = new();

    public static IEdmModel GetOrAdd(Type elementType, Action<ODataConventionModelBuilder>? configure)
        => Cache.GetOrAdd((elementType, configure), static key => Build(key.ElementType, key.Configure));

    private static IEdmModel Build(Type elementType, Action<ODataConventionModelBuilder>? configure)
    {
        var builder = new ODataConventionModelBuilder();
        builder.EnableLowerCamelCase();

        var entityType = builder.AddEntityType(elementType);
        builder.AddEntitySet(elementType.Name, entityType);

        configure?.Invoke(builder);

        // After the app's own customization, so an app that sets OnModelCreating keeps its hook —
        // and gets the last word, since it runs second.
        var configured = builder.OnModelCreating;
        builder.OnModelCreating = model =>
        {
            AllowNestedFiltering(model);
            configured?.Invoke(model);
        };

        try
        {
            return builder.GetEdmModel();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to build an EDM model for '{elementType.FullName}'. OData queries need an " +
                "entity type with a key; types without a conventional 'Id'/'‹Type›Id' property must " +
                "declare one via SynaxODataOptions.ConfigureModel (e.g. " +
                $"builder.EntityType<{elementType.Name}>().HasKey(x => x.SomeProperty)).",
                ex);
        }
    }

    /// <summary>
    /// Marks every discovered type filterable, which is what allows a <c>$filter</c> nested inside
    /// <c>$expand</c>. The root type's own <c>$filter</c> is permitted by
    /// <c>DefaultQueryConfigurations.EnableFilter</c>, but that flag is not consulted for the types
    /// an <c>$expand</c> reaches: without this, <c>$expand=tags($filter=label eq 'x')</c> is refused
    /// as "the property 'label' cannot be used in the $filter query option", which describes a
    /// model the app never wrote. Gating stays where it already is —
    /// <see cref="SynaxODataOptions.AllowedQueryOptions"/> and the other guardrails — so this only
    /// ever permits.
    /// </summary>
    /// <remarks>
    /// Runs inside <see cref="ODataConventionModelBuilder.OnModelCreating"/> because that is the one
    /// point where the conventions have discovered the navigation targets (a bare
    /// <c>AddEntityType</c> registers only the root) and the configuration is still writable.
    /// </remarks>
    private static void AllowNestedFiltering(ODataModelBuilder builder)
    {
        foreach (var type in builder.StructuralTypes)
        {
            var query = type.QueryConfiguration;

            // Asking for anything materializes the type's model-bound settings, whose MaxTop starts
            // at 0 — and 0 is a *limit*, not "unset", so leaving it would reject every $top. MaxTop
            // is SynaxODataOptions.MaxTop's job; only a positive value an app configured itself is
            // worth keeping.
            var maxTop = query.ModelBoundQuerySettings?.MaxTop;

            query.SetFilter(properties: null, enableFilter: true);
            query.SetMaxTop(maxTop is > 0 ? maxTop : null);
        }
    }
}
