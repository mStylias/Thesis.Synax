using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.UriParser;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Guardrails for the query options a client nests inside <c>$expand</c>
/// (<c>$expand=tags($filter=…;$orderby=…;$top=5;$select=…;$count=true)</c>).
/// </summary>
/// <remarks>
/// The package validates a nested clause for size and <c>$top</c>, but not for *permission*: the
/// <see cref="SynaxODataOptions.AllowedQueryOptions"/> set, <see cref="SynaxODataOptions.AllowedOrderByProperties"/>
/// and the <c>$compute</c>/<c>$search</c> switches are all applied to the request's own options and
/// never to the ones under an <c>$expand</c>. Left alone that makes <c>$expand</c> a way around every
/// one of them, and it makes a nested <c>$search</c> or <c>$apply</c> — which the expand binder drops
/// silently — look like it was honoured. Both are answered here with a <c>400</c> naming the option.
/// </remarks>
internal static class ExpandGuardrails
{
    public static void Validate<T>(ODataQueryOptions<T> options, SynaxODataOptions settings)
    {
        if (options.SelectExpand?.SelectExpandClause is not { } clause)
            return;

        Walk(clause, options, settings);
    }

    private static void Walk<T>(SelectExpandClause clause, ODataQueryOptions<T> options, SynaxODataOptions settings)
    {
        foreach (var item in clause.SelectedItems.OfType<ExpandedReferenceSelectItem>())
        {
            var elementType = ElementNameOf(item, options.Context.Model);

            ValidateItem(item, elementType, options, settings);

            if (item is not ExpandedNavigationSelectItem navigation)
                continue;

            ValidateLevels(navigation, settings, elementType);

            if (navigation.SelectAndExpand is not { } nested)
                continue;

            if (!nested.AllSelected)
                Require(settings, AllowedQueryOptions.Select, "$select");

            Walk(nested, options, settings);
        }
    }

    private static void ValidateItem<T>(
        ExpandedReferenceSelectItem item,
        string elementType,
        ODataQueryOptions<T> options,
        SynaxODataOptions settings)
    {
        if (item.FilterOption is not null)
            Require(settings, AllowedQueryOptions.Filter, "$filter");

        if (item.SkipOption is not null)
            Require(settings, AllowedQueryOptions.Skip, "$skip");

        if (item.TopOption is not null)
            Require(settings, AllowedQueryOptions.Top, "$top");

        if (item.CountOption is not null)
            Require(settings, AllowedQueryOptions.Count, "$count");

        if (item.OrderByOption is not null)
        {
            Require(settings, AllowedQueryOptions.OrderBy, "$orderby");
            ValidateOrderBy(item.OrderByOption, settings, elementType);
        }

        if (item.ComputeOption is not null)
        {
            if (!settings.EnableCompute)
                throw NotEnabled("$compute", nameof(SynaxODataOptions.EnableCompute));

            ComputeGuardrails.Validate(item.ComputeOption, settings);
        }

        // Not a permission question: the expand binder ignores both outright, so honouring the
        // client would mean returning rows that do not answer the query they sent.
        if (item.SearchOption is not null && !SearchGuardrails.HasBinder(options.Context))
            throw new SynaxODataQueryException(SearchGuardrails.NotAvailable(elementType));

        if (item.ApplyOption is not null)
            throw new SynaxODataQueryException(
                $"$apply is not supported inside $expand (on '{elementType}'): the expanded rows " +
                "are materialized as-is, so the aggregation would be silently ignored. Aggregate the " +
                "queried collection itself with a top-level $apply instead.");
    }

    /// <summary>
    /// Applies <see cref="SynaxODataOptions.AllowedOrderByProperties"/> to a nested <c>$orderby</c>.
    /// The names live in one pool across the exposed shape, which is the same pool the restriction
    /// was written against — an app that lists the columns it is willing to sort on has not agreed
    /// to sort an expanded collection by something else.
    /// </summary>
    private static void ValidateOrderBy(OrderByClause? clause, SynaxODataOptions settings, string elementType)
    {
        if (settings.AllowedOrderByProperties.Count == 0)
            return;

        for (var current = clause; current is not null; current = current.ThenBy)
        {
            var name = QueryNodes.PropertyName(current.Expression);

            if (name is null || !settings.AllowedOrderByProperties.Contains(name))
                throw new SynaxODataQueryException(
                    $"$orderby inside $expand (on '{elementType}') may only use " +
                    $"{string.Join(", ", settings.AllowedOrderByProperties.Order())}" +
                    (name is null ? "." : $"; '{name}' is not allowed."));
        }
    }

    private static void Require(SynaxODataOptions settings, AllowedQueryOptions option, string name)
    {
        if (!settings.AllowedQueryOptions.HasFlag(option))
            throw new SynaxODataQueryException(
                $"The query option {name} is not allowed, inside $expand or out of it.");
    }

    private static SynaxODataQueryException NotEnabled(string option, string switchName)
        => new($"The query option {option} is not allowed, inside $expand or out of it. " +
               $"Turn it on with SynaxODataOptions.{switchName}.");

    /// <summary>
    /// <c>$levels=max</c> means "as deep as this service allows", which the package resolves to
    /// <see cref="SynaxODataOptions.MaxExpansionDepth"/> — except that <c>0</c> is OData's way of
    /// saying "no depth limit", and an unbounded <c>max</c> over a self-referencing navigation walks
    /// the graph until it runs out of rows. Refused with the lever named rather than left to run.
    /// </summary>
    private static void ValidateLevels(
        ExpandedNavigationSelectItem item,
        SynaxODataOptions settings,
        string elementType)
    {
        if (item.LevelsOption is { IsMaxLevel: true } && settings.MaxExpansionDepth <= 0)
            throw new SynaxODataQueryException(
                $"$levels=max is not allowed on '{elementType}' while MaxExpansionDepth is unlimited: " +
                "there would be nothing to stop the expansion. Set SynaxODataOptions.MaxExpansionDepth, " +
                "or ask for a specific number of levels.");
    }

    /// <summary>
    /// The name of the type behind an expanded navigation, for error messages: the CLR name the app
    /// wrote where the model carries it (always, for a model this library built), else the EDM name.
    /// </summary>
    private static string ElementNameOf(ExpandedReferenceSelectItem item, IEdmModel model)
    {
        if (item.NavigationSource?.EntityType is not { } edmType)
            return "the expanded type";

        return model.GetAnnotationValue<ClrTypeAnnotation>(edmType)?.ClrType.Name ?? edmType.Name;
    }
}
