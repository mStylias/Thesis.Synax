using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.AspNetCore.OData.Query.Wrapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.OData;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Applies client-supplied OData query options (<c>$filter</c>, <c>$orderby</c>, <c>$top</c>,
/// <c>$skip</c>, <c>$count</c>, <c>$select</c>, <c>$expand</c>, and opt-in <c>$search</c>,
/// <c>$compute</c>, <c>$apply</c> and server-driven paging) to an <see cref="ISynaxQuery{T}"/> and
/// materializes the result. Standalone — usable with nothing but the repository query and an
/// <see cref="HttpRequest"/>; invalid client queries throw <see cref="SynaxODataQueryException"/>.
/// </summary>
public static class SynaxQueryODataExtensions
{
    /// <summary>
    /// Validates and applies <paramref name="options"/> to the query, then materializes one
    /// <see cref="ODataPage"/>. Options apply to the query's element type — compose base filters
    /// and projections first (e.g. <c>repo.QueryDto&lt;TDto&gt;()</c>), so clients only ever query
    /// the exposed shape.
    /// </summary>
    /// <exception cref="SynaxODataQueryException">
    /// The query options are syntactically invalid or violate the configured guardrails.
    /// </exception>
    public static Task<ODataPage> ToODataPageAsync<T>(
        this ISynaxQuery<T> query,
        ODataQueryOptions<T> options,
        SynaxODataOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= SynaxODataOptions.Default;
        return query.ExecuteAsync(q => ApplyAsync(q, options, settings, cancellationToken));
    }

    /// <summary>
    /// Convenience overload that builds the <see cref="ODataQueryOptions{T}"/> from the request's
    /// query string (using a cached EDM model for <typeparamref name="T"/>), then applies it.
    /// When <paramref name="settings"/> is omitted, the instance registered via
    /// <c>AddSynaxOData</c> is used, falling back to defaults.
    /// </summary>
    /// <exception cref="SynaxODataQueryException">
    /// The query options are syntactically invalid or violate the configured guardrails.
    /// </exception>
    public static Task<ODataPage> ToODataPageAsync<T>(
        this ISynaxQuery<T> query,
        HttpRequest request,
        SynaxODataOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= ResolveSettings(request);
        return query.ToODataPageAsync(CreateQueryOptions<T>(request, settings), settings, cancellationToken);
    }

    internal static SynaxODataOptions ResolveSettings(HttpRequest request)
        => request.HttpContext.RequestServices?.GetService(typeof(SynaxODataOptions)) as SynaxODataOptions
            ?? SynaxODataOptions.Default;

    /// <summary>
    /// Builds <see cref="ODataQueryOptions{T}"/> for the request against a cached EDM model, with
    /// all query capabilities enabled on the context (gating happens through the validation
    /// settings instead). Needs no OData routing or registration — outside an OData route the
    /// parser falls back to its own case-insensitive resolver.
    /// </summary>
    /// <remarks>
    /// When the settings declare searchable members, this also attaches the OData sub-container that
    /// holds Synax's <c>$search</c> binder to the request. That container is built (and cached on the
    /// settings instance) once per element type — one more reason to prefer a DI-registered
    /// <see cref="SynaxODataOptions"/> over one allocated per request.
    /// </remarks>
    /// <exception cref="SynaxODataQueryException">The request's query options fail to parse.</exception>
    public static ODataQueryOptions<T> CreateQueryOptions<T>(HttpRequest request, SynaxODataOptions? settings = null)
    {
        settings ??= SynaxODataOptions.Default;

        var element = settings.Elements.GetOrAdd(typeof(T));
        var context = new ODataQueryContext(element.Model, typeof(T), path: null);

        // A manually built context starts with every capability off (that's normally filled in by
        // OData routing's AddOData().Filter()... calls). Enable them all and let the validation
        // settings (AllowedQueryOptions, MaxTop, ...) do the actual gating.
        context.DefaultQueryConfigurations.EnableFilter = true;
        context.DefaultQueryConfigurations.EnableOrderBy = true;
        context.DefaultQueryConfigurations.EnableCount = true;
        context.DefaultQueryConfigurations.EnableSelect = true;
        context.DefaultQueryConfigurations.EnableExpand = true;
        context.DefaultQueryConfigurations.EnableSkipToken = true;
        context.DefaultQueryConfigurations.MaxTop = settings.MaxTop;

        // The parser reads binders off the request's OData container, and ODataQueryContext's own
        // RequestContainer setter is internal — so this assignment, before the options are built, is
        // the only way to inject one. A request already on a real OData route keeps its own
        // container; one Synax attached earlier (for a different element type, if a request runs two
        // OData queries) is replaced with the one for this type.
        var feature = request.HttpContext.ODataFeature();
        if (element.Services is not null && IsSynaxOwned(feature.Services))
            feature.Services = element.Services;

        try
        {
            return new ODataQueryOptions<T>(context, request);
        }
        catch (ODataException ex)
        {
            throw new SynaxODataQueryException($"The OData query is invalid: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Whether the request's OData container is absent or one Synax attached — i.e. safe to replace.
    /// Our containers are the only ones carrying a <see cref="SynaxSearchBinder"/>.
    /// </summary>
    private static bool IsSynaxOwned(IServiceProvider? services)
        => services is null || services.GetService(typeof(ISearchBinder)) is SynaxSearchBinder;

    private static async Task<ODataPage> ApplyAsync<T>(
        IQueryable<T> source,
        ODataQueryOptions<T> options,
        SynaxODataOptions settings,
        CancellationToken cancellationToken)
    {
        var querySettings = settings.ToQuerySettings();
        var rewriting = ODataExpressionRewriting.For(source, options.Request);

        long? totalCount = null;
        IReadOnlyList<object?> value;
        string? nextLink = null;

        try
        {
            SearchGuardrails.Validate(options, settings);
            GuardFormat(options);

            options.Validate(settings.ToValidationSettings());

            if (options.Compute is not null)
                ComputeGuardrails.Validate(options.Compute.ComputeClause, settings);

            if (options.Apply is not null)
                ApplyGuardrails.Validate(options, settings);

            if (options.SelectExpand is not null)
                ExpandGuardrails.Validate(options, settings);

            if (options.Count?.Value == true)
            {
                // ApplyTo never produces the count; count the narrowed set (before paging) ourselves.
                var countSource = rewriting.Apply(CountSource(source, options, settings, querySettings));

                totalCount = countSource is IQueryable<T> typed
                    ? await LongCountAsync<T>(typed, cancellationToken)
                    : await CountReshapedAsync(countSource, cancellationToken);
            }

            if (settings.PageSize is > 0)
                (value, nextLink) = await ApplyPagedAsync(
                    source, options, settings, settings.PageSize.Value, rewriting, cancellationToken);
            else
                value = await MaterializeAsync<T>(
                    rewriting.Apply(options.ApplyTo(source, querySettings)), cancellationToken);
        }
        catch (ODataException ex)
        {
            throw new SynaxODataQueryException(Describe(ex, options), ex);
        }

        return new ODataPage
        {
            Context = settings.EnableAnnotations ? ODataAnnotations.ContextUrl(options, settings) : null,
            Value = settings.EnableAnnotations ? ODataAnnotations.Annotate(value, options, settings) : value,
            Count = totalCount,
            NextLink = nextLink
        };
    }

    /// <summary>
    /// <c>$format</c> parses but is otherwise ignored by the pipeline, and this bridge answers JSON
    /// and only JSON — so a client asking for anything else would get JSON back and no indication
    /// that its request was not honoured.
    /// </summary>
    private static void GuardFormat<T>(ODataQueryOptions<T> options)
    {
        if (options.RawValues.Format is not { Length: > 0 } format)
            return;

        if (!format.Equals("json", StringComparison.OrdinalIgnoreCase) &&
            !format.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            throw new SynaxODataQueryException(
                $"$format={format} is not available: this endpoint answers JSON only. Drop the option " +
                "or send $format=json.");
    }

    /// <summary>
    /// The query <c>$count</c> counts: everything that narrows the set, nothing that pages or
    /// selects members. <c>$filter</c> alone goes through <see cref="FilterQueryOption"/> directly
    /// (the long-standing path, and the narrowest SQL); once <c>$search</c>, <c>$compute</c> or
    /// <c>$apply</c> is in play the full pipeline has to run it, because a filter may reference a
    /// computed alias and search narrows the set just as much as a filter does.
    /// </summary>
    /// <remarks>
    /// With <c>$apply</c> the count is therefore the number of rows the transformations produce —
    /// groups, not source rows — which is what the OData specification asks for and what a client
    /// paging through an aggregation needs. Counting an aggregation also costs a second
    /// <see cref="ODataQueryOptions{T}"/>: see <see cref="CountOptions{T}"/>.
    /// </remarks>
    private static IQueryable CountSource<T>(
        IQueryable<T> source,
        ODataQueryOptions<T> options,
        SynaxODataOptions settings,
        ODataQuerySettings querySettings)
    {
        if (options.Search is null && options.Compute is null && options.Apply is null)
            return options.Filter is null
                ? source
                : (IQueryable<T>)options.Filter.ApplyTo(source, querySettings);

        return CountOptions(options, settings).ApplyTo(
            source,
            querySettings,
            AllowedQueryOptions.Skip | AllowedQueryOptions.Top | AllowedQueryOptions.OrderBy |
            AllowedQueryOptions.Select | AllowedQueryOptions.Expand | AllowedQueryOptions.SkipToken);
    }

    /// <summary>
    /// The options to count with. Applying a <c>$apply</c> rewrites its own
    /// <see cref="ODataQueryContext.ElementClrType"/> to the wrapper type the transformations
    /// produce, which makes the options single-use: a second <c>ApplyTo</c> binds the client's
    /// grouping against the wrapper and throws. Counting therefore parses the request a second time
    /// against a fresh context — cheap next to the query it is about to run, and only when both
    /// <c>$apply</c> and <c>$count=true</c> are present.
    /// </summary>
    private static ODataQueryOptions<T> CountOptions<T>(ODataQueryOptions<T> options, SynaxODataOptions settings)
        => options.Apply is null ? options : CreateQueryOptions<T>(options.Request, settings);

    private static Task<long> LongCountAsync<TElement>(IQueryable source, CancellationToken cancellationToken)
    {
        var typed = (IQueryable<TElement>)source;

        return typed.Provider is IAsyncQueryProvider
            ? typed.LongCountAsync(cancellationToken)
            : Task.FromResult(typed.LongCount());
    }

    private static readonly MethodInfo LongCountMethod = typeof(SynaxQueryODataExtensions)
        .GetMethod(nameof(LongCountAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Func<IQueryable, CancellationToken, Task<long>>> Counters = new();

    /// <summary>
    /// Counts a queryable whose element type is only known at runtime — the aggregation wrappers
    /// <c>$apply</c> produces. The closed delegate is cached per element type, so the reflection
    /// happens once per shape rather than once per request.
    /// </summary>
    private static Task<long> CountReshapedAsync(IQueryable source, CancellationToken cancellationToken)
        => Counters.GetOrAdd(
            source.ElementType,
            static elementType => LongCountMethod
                .MakeGenericMethod(elementType)
                .CreateDelegate<Func<IQueryable, CancellationToken, Task<long>>>())(source, cancellationToken);

    /// <summary>
    /// The message for a client error, with the one piece of context the OData binder's own wording
    /// leaves out: after a reshaping <c>$apply</c>, <c>$filter</c> and <c>$orderby</c> bind against
    /// the transformation's result, so the binder reports a member of the *queried* type as missing.
    /// </summary>
    private static string Describe<T>(ODataException exception, ODataQueryOptions<T> options)
    {
        var message = $"The OData query is invalid: {exception.Message}";

        try
        {
            // Reading the clause parses it, so the failure being reported may well be this one.
            return options.Apply is { ApplyClause: var clause } && ApplyGuardrails.Reshapes(clause)
                ? message + " " + ApplyGuardrails.DescribeShape(clause)
                : message;
        }
        catch (ODataException)
        {
            return message;
        }
    }

    /// <summary>
    /// Materializes one server-driven page. Rows a <c>$apply</c> reshaped are paged by offset — an
    /// aggregation result has no key to encode — and everything else by keyset cursor.
    /// </summary>
    private static Task<(IReadOnlyList<object?> Value, string? NextLink)> ApplyPagedAsync<T>(
        IQueryable<T> source,
        ODataQueryOptions<T> options,
        SynaxODataOptions settings,
        int pageSize,
        ODataExpressionRewriting rewriting,
        CancellationToken cancellationToken)
        => options.Apply is { } apply && ApplyGuardrails.Reshapes(apply.ApplyClause)
            ? ApplyPagedByOffsetAsync(source, options, settings, pageSize, rewriting, cancellationToken)
            : ApplyPagedByCursorAsync(source, options, settings, pageSize, rewriting, cancellationToken);

    /// <summary>
    /// Reads <c>pageSize + 1</c> rows, keeps <c>pageSize</c> of them, and mints an
    /// <c>@odata.nextLink</c> carrying a keyset <c>$skiptoken</c> when the extra row showed up.
    /// (<see cref="ODataQuerySettings.PageSize"/> alone truncates without reporting that it did, so
    /// "is there another page" has to be answered here.)
    /// </summary>
    /// <remarks>
    /// <c>$select</c>/<c>$expand</c> are held back from the database query and applied to the
    /// materialized page instead: the cursor is read off the last row, and a reshaped row only
    /// carries the members the client selected — which need not include the sort keys. The cost is a
    /// full-column read for a selecting client; the alternative is rejecting the combination.
    /// </remarks>
    private static async Task<(IReadOnlyList<object?> Value, string? NextLink)> ApplyPagedByCursorAsync<T>(
        IQueryable<T> source,
        ODataQueryOptions<T> options,
        SynaxODataOptions settings,
        int pageSize,
        ODataExpressionRewriting rewriting,
        CancellationToken cancellationToken)
    {
        // Resolved before querying so an unpageable ordering or a bad cursor is a 400, not a wrong
        // page and not a 500 from deep inside the skip-token handler.
        var keys = ServerDrivenPaging.ResolveKeys(options);

        if (options.RawValues.SkipToken is { Length: > 0 } token)
            ServerDrivenPaging.ValidateToken(token, keys);

        var pageSettings = settings.ToQuerySettings();
        pageSettings.PageSize = pageSize + 1;

        var applied = (IQueryable<T>)rewriting.Apply(options.ApplyTo(
            source,
            pageSettings,
            AllowedQueryOptions.Select | AllowedQueryOptions.Expand));

        var rows = await MaterializeRowsAsync(applied, cancellationToken);
        var hasMore = Trim(rows, pageSize);

        var querySettings = settings.ToQuerySettings();

        var value = options.SelectExpand is null
            ? rows
            : await MaterializeAsync<T>(
                options.SelectExpand.ApplyTo(rows.Cast<T>().AsQueryable(), querySettings),
                cancellationToken);

        var nextLink = hasMore
            ? ServerDrivenPaging.CreateNextLink(
                options.Request,
                ServerDrivenPaging.KeysetCursor(rows[^1]!, keys, options.Context.Model),
                pageSize,
                settings,
                options.Top?.Value)
            : null;

        return (value, nextLink);
    }

    /// <summary>
    /// The same <c>pageSize + 1</c> read for rows a grouping, aggregation or compute produced, with
    /// the next link carrying a <c>$skip</c> offset instead of a cursor. Those rows have neither the
    /// queried type's key nor a stable sort value to encode, and the ordering the transformations
    /// imply is deterministic for a given set of rows — so an offset pages them correctly, at the
    /// usual offset-paging cost of shifting if the underlying rows change between pages.
    /// </summary>
    private static async Task<(IReadOnlyList<object?> Value, string? NextLink)> ApplyPagedByOffsetAsync<T>(
        IQueryable<T> source,
        ODataQueryOptions<T> options,
        SynaxODataOptions settings,
        int pageSize,
        ODataExpressionRewriting rewriting,
        CancellationToken cancellationToken)
    {
        var pageSettings = settings.ToQuerySettings();
        pageSettings.PageSize = pageSize + 1;

        var applied = rewriting.Apply(options.ApplyTo(source, pageSettings));

        var rows = await MaterializeRowsAsync(applied, cancellationToken);
        var hasMore = Trim(rows, pageSize);

        var nextLink = hasMore
            ? ServerDrivenPaging.CreateNextLink(
                options.Request,
                ServerDrivenPaging.OffsetCursor(options.Skip?.Value, pageSize),
                pageSize,
                settings,
                options.Top?.Value)
            : null;

        return (Shape<T>(applied, rows), nextLink);
    }

    /// <summary>Drops the extra probe row, reporting whether there was one.</summary>
    private static bool Trim(List<object?> rows, int pageSize)
    {
        if (rows.Count <= pageSize)
            return false;

        rows.RemoveRange(pageSize, rows.Count - pageSize);
        return true;
    }

    private static async Task<IReadOnlyList<object?>> MaterializeAsync<T>(
        IQueryable applied,
        CancellationToken cancellationToken)
        => Shape<T>(applied, await MaterializeRowsAsync(applied, cancellationToken));

    /// <summary>
    /// Rows as the applied query produced them, or — when <c>$select</c>/<c>$expand</c>/<c>$apply</c>
    /// reshaped them into wrappers — projected to plain dictionaries so the payload serializes
    /// without OData formatters.
    /// </summary>
    private static IReadOnlyList<object?> Shape<T>(IQueryable applied, List<object?> rows)
        => applied is IQueryable<T> ? rows : rows.ConvertAll(NormalizeValue);

    private static async Task<List<object?>> MaterializeRowsAsync(
        IQueryable applied,
        CancellationToken cancellationToken)
    {
        var rows = new List<object?>();

        if (applied.Provider is IAsyncQueryProvider && applied is IAsyncEnumerable<object?> asyncRows)
        {
            await foreach (var row in asyncRows.WithCancellation(cancellationToken))
                rows.Add(row);
        }
        else
        {
            foreach (var row in applied)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>
    /// Recursively converts wrapper values (and collections of them, produced by nested
    /// <c>$expand</c>) into plain dictionaries/lists; passes everything else through untouched.
    /// </summary>
    internal static object? NormalizeValue(object? value) => value switch
    {
        null => null,
        ISelectExpandWrapper wrapper => Normalize(wrapper.ToDictionary()),
        // $apply and computed-select rows come back as dynamic wrappers whose Values dictionary is
        // already EDM-cased; without this they would serialize as {}.
        DynamicTypeWrapper wrapper => Normalize(wrapper.Values),
        string or byte[] => value,
        IEnumerable enumerable => enumerable.Cast<object?>().Select(NormalizeValue).ToList(),
        _ => value
    };

    /// <summary>
    /// One wrapper's members, normalized — plus the <c>member@odata.count</c> entry that answers a
    /// <c>$count=true</c> nested in an <c>$expand</c>. The count rides on the collection itself
    /// (<see cref="ICountOptionCollection"/>) and is the total *before* a nested <c>$top</c>/<c>$skip</c>
    /// windowed it, so folding it into the collection's own rows would lose it. The annotation name
    /// is the OData wire form, so a generic client reads it without knowing anything about Synax.
    /// </summary>
    private static Dictionary<string, object?> Normalize(IDictionary<string, object?> members)
    {
        var normalized = new Dictionary<string, object?>(members.Count);

        foreach (var (name, value) in members)
        {
            normalized[name] = NormalizeValue(value);

            if (value is ICountOptionCollection { TotalCount: { } total })
                normalized[name + "@odata.count"] = total;
        }

        return normalized;
    }
}
