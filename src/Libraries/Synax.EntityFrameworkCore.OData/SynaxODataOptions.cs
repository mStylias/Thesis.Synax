using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.OData.ModelBuilder;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Guardrails and model configuration for OData query application. Register a shared instance via
/// <c>services.AddSynaxOData(...)</c>, or pass one explicitly per call; when neither is provided,
/// the defaults below apply.
/// </summary>
/// <remarks>
/// Every capability beyond <c>$filter/$orderby/$top/$skip/$count/$select/$expand</c> is off until
/// switched on here, and each has exactly one switch: <see cref="Searchable{T}"/> for
/// <c>$search</c>, <see cref="EnableCompute"/> for <c>$compute</c>, <see cref="EnableApply"/> for
/// <c>$apply</c>, <see cref="PageSize"/> for server-driven paging (<c>@odata.nextLink</c> +
/// <c>$skiptoken</c>), and <see cref="EnableAnnotations"/> for the <c>@odata.context</c>/
/// <c>@odata.id</c>/<c>@odata.type</c> annotations. Turning one on adds its flag to
/// <see cref="AllowedQueryOptions"/>; a client sending a query option nothing switched on gets a
/// <c>400</c>. <see cref="Expose{T}"/> is the separate declaration that publishes a type through
/// <c>$metadata</c> and the service document.
/// </remarks>
public sealed class SynaxODataOptions
{
    /// <summary>The maximum value a client may request via <c>$top</c> (default 100).</summary>
    public int MaxTop { get; set; } = 100;

    /// <summary>
    /// The query options clients are allowed to use. Defaults to
    /// <c>$filter</c>, <c>$orderby</c>, <c>$top</c>, <c>$skip</c>, <c>$count</c>, <c>$select</c>
    /// and <c>$expand</c>. The opt-in capabilities (<see cref="Searchable{T}"/>,
    /// <see cref="EnableCompute"/>, <see cref="EnableApply"/>, <see cref="PageSize"/>) add their own
    /// flags on top of this set.
    /// </summary>
    public AllowedQueryOptions AllowedQueryOptions { get; set; } =
        Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Filter |
        Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.OrderBy |
        Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Top |
        Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Skip |
        Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Count |
        Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Select |
        Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Expand;

    /// <summary>The maximum <c>$expand</c> nesting depth (default 2).</summary>
    public int MaxExpansionDepth { get; set; } = 2;

    /// <summary>
    /// The maximum number of nodes in a <c>$filter</c>, <c>$compute</c> or <c>$apply</c> syntax tree
    /// (default 100). For <c>$apply</c> the whole transformation chain is counted together.
    /// </summary>
    public int MaxNodeCount { get; set; } = 100;

    /// <summary>
    /// When non-empty, restricts <c>$orderby</c> to these properties. Names must match the EDM
    /// (camelCase) form clients use, e.g. <c>"createdAt"</c>. Empty (the default) allows all.
    /// </summary>
    public ISet<string> AllowedOrderByProperties { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Allows <c>$compute</c>: clients may define named expressions over the exposed shape
    /// (<c>$compute=price mul quantity as total</c>) and then <c>$filter</c>, <c>$orderby</c> or
    /// <c>$select</c> them. Off by default.
    /// </summary>
    /// <remarks>
    /// A computed value is invisible unless the client also <c>$select</c>s it — <c>$compute</c>
    /// alone leaves the response shape untouched. Expression size is bounded by
    /// <see cref="MaxNodeCount"/>, and <see cref="AllowedComputeProperties"/> can narrow what a
    /// compute expression is allowed to read.
    /// </remarks>
    public bool EnableCompute { get; set; }

    /// <summary>
    /// When non-empty, restricts which properties a <c>$compute</c> expression may read. Names must
    /// match the EDM (camelCase) form, as in <see cref="AllowedOrderByProperties"/>. Empty (the
    /// default) allows all of the exposed shape.
    /// </summary>
    public ISet<string> AllowedComputeProperties { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Allows <c>$apply</c>: clients may aggregate over the exposed shape
    /// (<c>$apply=groupby((category/name),aggregate(price with sum as revenue))</c>), which the
    /// database answers with a <c>GROUP BY</c>. Off by default.
    /// </summary>
    /// <remarks>
    /// Supported transformations are <c>filter</c>, <c>compute</c>, <c>groupby</c> and
    /// <c>aggregate</c> (<c>sum</c>/<c>min</c>/<c>max</c>/<c>average</c>/<c>countdistinct</c>/
    /// <c>$count</c>), chained with <c>/</c>. Rows produced by a grouping, aggregation or compute are
    /// no longer the queried type but dictionaries of the members the transformations named, so
    /// <c>$select</c>, <c>$expand</c>, <c>$search</c> and <c>$skiptoken</c> are a <c>400</c>
    /// alongside such a query — the OData pipeline would otherwise ignore them silently. Size is
    /// bounded by <see cref="MaxNodeCount"/> and <see cref="MaxAggregationDepth"/>, and
    /// <see cref="AllowedAggregations"/> can narrow which methods clients may use.
    /// </remarks>
    public bool EnableApply { get; set; }

    /// <summary>
    /// When non-empty, restricts which aggregation methods a <c>$apply</c> may use. Names are the
    /// ones clients write, case-insensitively: <c>sum</c>, <c>min</c>, <c>max</c>, <c>average</c>,
    /// <c>countdistinct</c> and <c>$count</c>. Empty (the default) allows all of them.
    /// </summary>
    public ISet<string> AllowedAggregations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The maximum number of transformations in a <c>$apply</c> chain (default 4). Each one nests
    /// another query around the previous, so this bounds how deeply a client may nest;
    /// <c>groupby((x),aggregate(y with sum as z))</c> counts as two.
    /// </summary>
    public int MaxAggregationDepth { get; set; } = 4;

    /// <summary>
    /// Enables server-driven paging: responses carry at most this many rows plus an
    /// <c>@odata.nextLink</c> the client follows for the next page, and <c>$skiptoken</c> becomes an
    /// allowed query option. <c>null</c> (the default) leaves paging entirely client-driven via
    /// <c>$top</c>/<c>$skip</c>.
    /// </summary>
    /// <remarks>
    /// The cursor is keyset-shaped (it encodes the last row's sort key), so pages neither duplicate
    /// nor skip rows when the table is written to between requests. It requires an <c>$orderby</c>
    /// over the queried type's own properties — a nested or computed sort key is a <c>400</c>. A
    /// <c>$top</c> at or below the page size wins and suppresses the link; a larger one is reduced
    /// per page so following the links never exceeds it. <c>$count</c> still reports the full
    /// filtered total, not the page.
    /// </remarks>
    public int? PageSize { get; set; }

    /// <summary>
    /// The absolute service root used to render <c>@odata.nextLink</c>. When <c>null</c> (the
    /// default) the link is host-relative (<c>/api/v1/things/search?...</c>), which every OData
    /// client resolves against the request URL. Set it when absolute links are needed — behind a
    /// reverse proxy, for instance, where the request's own host is not the public one.
    /// </summary>
    public Uri? ServiceRoot { get; set; }

    /// <summary>
    /// Publishes <typeparamref name="T"/> as an entity set named <paramref name="entitySetName"/>,
    /// which is what puts it in <c>$metadata</c> and the service document served by
    /// <c>app.MapSynaxODataMetadata()</c>, and what <c>@odata.context</c> points at.
    /// </summary>
    /// <remarks>
    /// The name is part of a published contract, so it is given rather than guessed: one type gets
    /// one name, one name belongs to one type, and declaring the same pair twice is a no-op (two
    /// modules may declare a shared type). Exposing a type does <em>not</em> change how any query
    /// behaves — queries are still parsed against the per-type model they always were; the composed
    /// model these declarations build exists to be published, not to be queried.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The type is already exposed under a different name, the name already belongs to another type,
    /// or the model has already been published.
    /// </exception>
    public SynaxODataOptions Expose<T>(string entitySetName)
    {
        Registry.Add(typeof(T), entitySetName);
        return this;
    }

    /// <summary>
    /// Adds the OData control information to responses: <c>@odata.context</c> on the envelope, and
    /// <c>@odata.id</c>/<c>@odata.type</c> on each row. Off by default, so existing payloads are
    /// unchanged. Needs the queried type to be <see cref="Expose{T}"/>d and the service root to be
    /// known — either <see cref="ServiceRoot"/> or an <c>app.MapSynaxODataMetadata()</c> call.
    /// </summary>
    /// <remarks>
    /// Row annotations are extra members, so a row has to be a dictionary to carry them: with this
    /// on, plain typed rows come back in the same camel-cased dictionary form <c>$select</c> already
    /// produces. Rows a <c>$apply</c> reshaped are left alone — an aggregation result is not an
    /// entity, so it has neither an id nor a type — and <c>@odata.count</c> is unaffected, having
    /// always been part of the <c>$count</c> answer rather than of this control information.
    /// </remarks>
    public bool EnableAnnotations { get; set; }

    /// <summary>
    /// Customizes the EDM model built for each queried type — e.g. declare a key for a DTO whose
    /// key isn't discovered by convention (<c>builder.EntityType&lt;MyDto&gt;().HasKey(d =&gt; d.Code)</c>).
    /// Models are cached per (type, delegate) pair, so prefer a stable delegate (DI-registered
    /// options, method group, or non-capturing lambda) over one allocated per request.
    /// </summary>
    public Action<ODataConventionModelBuilder>? ConfigureModel { get; set; }

    /// <summary>
    /// Declares the string members <c>$search</c> may look in for <typeparamref name="T"/>, which is
    /// also what allows <c>$search</c> at all: a client searching a type with no declared members
    /// gets a <c>400</c> naming the type, never a silent match-everything.
    /// </summary>
    /// <remarks>
    /// A search term matches when any declared member case-insensitively contains it
    /// (<c>lower(col) LIKE '%term%'</c> in SQL — correct but not index-backed; reach for
    /// <c>pg_trgm</c> or a <c>tsvector</c> column if a real workload needs one). Multiple terms
    /// combine with the client's <c>AND</c>/<c>OR</c>/<c>NOT</c>; a quoted phrase is one term.
    /// Members must be property or field access chains ending in a string
    /// (<c>x =&gt; x.Name</c>, <c>x =&gt; x.Category.Name</c>); calls declaring the same member twice
    /// are idempotent, so two modules can declare a shared type independently.
    /// </remarks>
    /// <exception cref="ArgumentException">A member is not a string-valued member access.</exception>
    public SynaxODataOptions Searchable<T>(params Expression<Func<T, string?>>[] members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var set = _searchable.GetOrAdd(typeof(T), static _ => new SearchableMemberSet());
        foreach (var member in members)
            set.Add(member);

        return this;
    }

    private readonly ConcurrentDictionary<Type, SearchableMemberSet> _searchable = new();

    internal static readonly SynaxODataOptions Default = new();

    internal ODataElementContextCache Elements { get; }

    /// <summary>The exposed types and the composed model published from them.</summary>
    internal ODataModelRegistry Registry { get; }

    /// <summary>
    /// The service root path recorded by <c>app.MapSynaxODataMetadata(prefix)</c>, used to render
    /// <c>@odata.context</c> when <see cref="ServiceRoot"/> is not set. Host-relative, and only ever
    /// written once at startup.
    /// </summary>
    internal string? MetadataPath { get; set; }

    /// <summary>Whether any type has searchable members — i.e. whether <c>$search</c> is on at all.</summary>
    internal bool HasSearchableMembers => !_searchable.IsEmpty;

    internal IReadOnlyList<LambdaExpression> SearchableMembersFor(Type elementType)
        => _searchable.TryGetValue(elementType, out var set) ? set.Members : [];

    public SynaxODataOptions()
    {
        Elements = new ODataElementContextCache(this);
        Registry = new ODataModelRegistry(this);
    }

    internal ODataValidationSettings ToValidationSettings()
    {
        var allowed = AllowedQueryOptions;

        if (HasSearchableMembers)
            allowed |= Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Search;

        if (EnableCompute)
            allowed |= Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Compute;

        if (EnableApply)
            allowed |= Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Apply;

        if (PageSize is > 0)
            allowed |= Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.SkipToken;

        // $format is always let past the validator so Synax can answer it itself: the validator's
        // refusal says only "not allowed", where the honest answer names the one format this bridge
        // speaks. A $format the endpoint cannot produce is still a 400 — see GuardFormat.
        allowed |= Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Format;

        var settings = new ODataValidationSettings
        {
            MaxTop = MaxTop,
            AllowedQueryOptions = allowed,
            MaxExpansionDepth = MaxExpansionDepth,
            MaxNodeCount = MaxNodeCount
        };

        foreach (var property in AllowedOrderByProperties)
            settings.AllowedOrderByProperties.Add(property);

        return settings;
    }

    internal ODataQuerySettings ToQuerySettings() => new()
    {
        // Stable ordering keeps $top/$skip windows deterministic even without $orderby.
        EnsureStableOrdering = true,
        // Default auto-detects the provider: no null propagation for EF (SQL handles nulls),
        // null propagation for LINQ-to-Objects (unit tests over in-memory data).
        HandleNullPropagation = HandleNullPropagationOption.Default,
        // Honour a $filter nested under a single-valued navigation ($expand=category($filter=...)),
        // which the pipeline otherwise drops on the floor — the member comes back null when the
        // filter does not match, which is what the client asked for and what the collection case
        // already does. Off by default in the package.
        HandleReferenceNavigationPropertyExpandFilter = true,
        TimeZone = TimeZoneInfo.Utc
    };
}
