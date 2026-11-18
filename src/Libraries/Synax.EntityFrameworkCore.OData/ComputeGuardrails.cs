using Microsoft.OData.UriParser;
using Microsoft.OData.UriParser.Aggregation;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Guardrails for <c>$compute</c>. The OData validators cover <c>$filter</c> but leave a compute
/// expression unbounded — it is a client-supplied expression evaluated per row, so its size and the
/// properties it may read need the same treatment <c>$filter</c> already gets from
/// <see cref="SynaxODataOptions.MaxNodeCount"/> and <see cref="SynaxODataOptions.AllowedOrderByProperties"/>.
/// </summary>
internal static class ComputeGuardrails
{
    /// <summary>
    /// Rejects a compute clause whose expressions exceed <see cref="SynaxODataOptions.MaxNodeCount"/>
    /// nodes, or that read a property outside
    /// <see cref="SynaxODataOptions.AllowedComputeProperties"/> when that set is non-empty.
    /// </summary>
    /// <exception cref="SynaxODataQueryException">The clause violates a guardrail.</exception>
    public static void Validate(ComputeClause clause, SynaxODataOptions settings)
    {
        var restricted = settings.AllowedComputeProperties.Count > 0;

        foreach (var item in clause.ComputedItems)
        {
            var nodes = 0;
            Walk(item, item.Expression, ref nodes, settings, restricted);
        }
    }

    private static void Walk(
        ComputeExpression item,
        QueryNode node,
        ref int nodes,
        SynaxODataOptions settings,
        bool restricted)
    {
        if (++nodes > settings.MaxNodeCount)
            throw new SynaxODataQueryException(
                $"The $compute expression for '{item.Alias}' is too complex: it exceeds the " +
                $"{settings.MaxNodeCount}-node limit (SynaxODataOptions.MaxNodeCount).");

        if (QueryNodes.PropertyName(node) is { } property)
            Require(item, property, settings, restricted);

        var children = QueryNodes.Children(node);
        if (children is null)
        {
            // An unrecognized node could hide a property reference from the allow-list check, so it
            // is a client error whenever the allow-list is the thing being enforced.
            if (restricted)
                throw new SynaxODataQueryException(
                    $"The $compute expression for '{item.Alias}' uses an unsupported construct " +
                    $"('{node.Kind}') that cannot be checked against " +
                    "SynaxODataOptions.AllowedComputeProperties.");

            return;
        }

        foreach (var child in children)
            Walk(item, child, ref nodes, settings, restricted);
    }

    private static void Require(
        ComputeExpression item,
        string property,
        SynaxODataOptions settings,
        bool restricted)
    {
        if (!restricted || settings.AllowedComputeProperties.Contains(property))
            return;

        throw new SynaxODataQueryException(
            $"The $compute expression for '{item.Alias}' reads '{property}', which is not in " +
            "SynaxODataOptions.AllowedComputeProperties. Allowed: " +
            string.Join(", ", settings.AllowedComputeProperties.Order(StringComparer.Ordinal)) + ".");
    }
}
