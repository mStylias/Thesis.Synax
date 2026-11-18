using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.UriParser;
using Microsoft.OData.UriParser.Aggregation;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Guardrails for <c>$apply</c>. Two jobs: bound the client-supplied transformation chain (the OData
/// validators gate the option but, as with <c>$compute</c>, never walk the expressions inside it),
/// and reject the combinations that have no defined meaning once a transformation has reshaped rows
/// away from the queried type — each of which the OData pipeline otherwise answers by silently
/// ignoring the other option, or by throwing something that is not a client error.
/// </summary>
internal static class ApplyGuardrails
{
    /// <summary>
    /// Whether the clause replaces rows with aggregation/compute wrappers rather than leaving them
    /// as the queried type. <c>filter(...)</c> alone does not; anything that groups, aggregates or
    /// computes does.
    /// </summary>
    public static bool Reshapes(ApplyClause clause)
        => clause.Transformations.Any(node =>
            node.Kind is TransformationNodeKind.GroupBy
                or TransformationNodeKind.Aggregate
                or TransformationNodeKind.Compute);

    /// <summary>
    /// Validates the transformation chain and its interaction with the rest of the query.
    /// </summary>
    /// <exception cref="SynaxODataQueryException">
    /// A guardrail is violated, or the query pairs <c>$apply</c> with an option that cannot be
    /// honoured over its result.
    /// </exception>
    public static void Validate<T>(ODataQueryOptions<T> options, SynaxODataOptions settings)
    {
        var clause = options.Apply!.ApplyClause;

        var nodes = 0;
        WalkChain(clause.Transformations, settings, ref nodes);

        var depth = Depth(clause.Transformations);
        if (depth > settings.MaxAggregationDepth)
            throw new SynaxODataQueryException(
                $"The $apply chain is {depth} transformations deep, above the limit of " +
                $"{settings.MaxAggregationDepth} (SynaxODataOptions.MaxAggregationDepth). Each " +
                "transformation nests another query around the previous one.");

        ValidateCombinations(options, settings, clause);
    }

    private static void ValidateCombinations<T>(
        ODataQueryOptions<T> options,
        SynaxODataOptions settings,
        ApplyClause clause)
    {
        // Both define values over the exposed shape, and the two parsers disagree about which one
        // owns the row: the OData pipeline resolves $compute against the pre-$apply type and fails
        // with a message about grouping expressions. compute(...) inside $apply is the same feature.
        if (options.RawValues.Compute is { Length: > 0 })
            throw new SynaxODataQueryException(
                "$compute cannot be combined with $apply. Use the compute(...) transformation inside " +
                "$apply instead, e.g. $apply=compute(price mul quantity as total)/groupby((name)," +
                "aggregate(total with sum as revenue)).");

        if (!Reshapes(clause))
            return;

        // Everything below is only a conflict because the rows are no longer T: they are aggregation
        // wrappers whose members are the grouping properties and the aggregate aliases.
        Reject(options.RawValues.Select, "$select", clause,
            "the response rows are already the shape the transformations produced. Choose the members " +
            "in the $apply itself — groupby((name),aggregate(...)) returns exactly those.");

        Reject(options.RawValues.Expand, "$expand", clause,
            "an aggregation result has no navigation properties to expand. Group over the related " +
            "property instead, e.g. groupby((category/name)).");

        Reject(options.RawValues.Search, "$search", clause,
            "$search is applied after $apply, and the searchable members are declared on the queried " +
            "type, not on the aggregation result. Narrow the rows with a filter(...) transformation " +
            "inside $apply instead.");

        Reject(options.RawValues.SkipToken, "$skiptoken", clause,
            "a keyset cursor needs the queried type's own key, which an aggregation result does not " +
            "carry. Server-driven paging over an aggregation uses $skip instead — follow the " +
            "@odata.nextLink rather than composing a cursor by hand.");

        // The validator silently skips AllowedOrderByProperties once $apply is present (the ordering
        // is resolved against the reshaped rows, which it cannot check), so allowing the pair would
        // make a configured restriction bypassable by adding a transformation.
        if (settings.AllowedOrderByProperties.Count > 0 && options.RawValues.OrderBy is { Length: > 0 })
            throw new SynaxODataQueryException(
                "$orderby cannot be combined with a reshaping $apply while " +
                "SynaxODataOptions.AllowedOrderByProperties restricts ordering: the ordering applies " +
                "to the aggregation result, whose members are not the properties that set describes. " +
                "Order inside the $apply's own shape by dropping either the restriction or the $orderby.");
    }

    private static void Reject(string? rawValue, string option, ApplyClause clause, string because)
    {
        if (string.IsNullOrEmpty(rawValue))
            return;

        throw new SynaxODataQueryException(
            $"{option} cannot be combined with a $apply that groups, aggregates or computes: " +
            because + " " + DescribeShape(clause));
    }

    /// <summary>
    /// The members a reshaping <c>$apply</c> leaves behind, as a sentence — the one piece of context
    /// that makes an otherwise cryptic binder error actionable, since <c>$filter</c> and
    /// <c>$orderby</c> are resolved against these names and nothing else.
    /// </summary>
    public static string DescribeShape(ApplyClause clause)
    {
        var names = AvailableNames(clause);

        return names is null
            ? "The rows keep the queried type's own members."
            : names.Count == 0
                ? "The $apply leaves no named members to reference."
                : "After this $apply the rows have these members, and nothing else: " +
                  string.Join(", ", names) + ".";
    }

    /// <summary>
    /// The member names available after the chain, or <c>null</c> when the rows still carry the
    /// queried type's own members (so no restriction can be stated).
    /// </summary>
    private static List<string>? AvailableNames(ApplyClause clause)
    {
        List<string>? names = null;

        foreach (var node in clause.Transformations)
            switch (node)
            {
                case GroupByTransformationNode groupBy:
                    names = [
                        ..groupBy.GroupingProperties.Select(property => property.Name),
                        ..Aliases(groupBy.ChildTransformations)
                    ];
                    break;

                case AggregateTransformationNode aggregate:
                    names = [..aggregate.AggregateExpressions.Select(expression => expression.Alias)];
                    break;

                // A compute keeps whatever came before and adds its aliases; over the untouched
                // entity shape there is still nothing to enumerate.
                case ComputeTransformationNode compute when names is not null:
                    names = [..names, ..compute.Expressions.Select(expression => expression.Alias)];
                    break;
            }

        return names;
    }

    private static IEnumerable<string> Aliases(TransformationNode? node)
        => node is AggregateTransformationNode aggregate
            ? aggregate.AggregateExpressions.Select(expression => expression.Alias)
            : [];

    /// <summary>
    /// The length of the longest transformation path: the chain itself plus the deepest child chain
    /// a <c>groupby</c> carries. <c>groupby((x),aggregate(y with sum as z))</c> is 2.
    /// </summary>
    private static int Depth(IEnumerable<TransformationNode> chain)
    {
        var nodes = chain.ToList();
        var deepest = 0;

        foreach (var node in nodes)
            if (node is GroupByTransformationNode { ChildTransformations: { } child })
                deepest = Math.Max(deepest, Depth([child]));

        return nodes.Count + deepest;
    }

    private static void WalkChain(
        IEnumerable<TransformationNode> chain,
        SynaxODataOptions settings,
        ref int nodes)
    {
        foreach (var node in chain)
            switch (node)
            {
                case FilterTransformationNode filter:
                    Count(filter.FilterClause.Expression, settings, ref nodes);
                    break;

                case ComputeTransformationNode compute:
                    foreach (var expression in compute.Expressions)
                        Count(expression.Expression, settings, ref nodes);
                    break;

                case GroupByTransformationNode groupBy:
                    foreach (var property in groupBy.GroupingProperties)
                        CountGrouping(property, settings, ref nodes);

                    if (groupBy.ChildTransformations is { } child)
                        WalkChain([child], settings, ref nodes);
                    break;

                case AggregateTransformationNode aggregate:
                    foreach (var expression in aggregate.AggregateExpressions)
                        ValidateAggregate(expression, settings, ref nodes);
                    break;

                case ExpandTransformationNode:
                    // The transformation only filters expanded entities, which this bridge does not
                    // serialize as part of an aggregation — it would be a silent no-op.
                    throw new SynaxODataQueryException(
                        "The expand(...) transformation inside $apply is not supported. Expand a " +
                        "collection with $expand (optionally with a nested $filter) instead, or group " +
                        "over the related property with groupby((category/name)).");

                default:
                    throw new SynaxODataQueryException(
                        $"The $apply transformation '{node.Kind}' is not supported. Supported: " +
                        "filter, compute, groupby and aggregate.");
            }
    }

    private static void CountGrouping(
        GroupByPropertyNode property,
        SynaxODataOptions settings,
        ref int nodes)
    {
        Count(property.Expression, settings, ref nodes);

        foreach (var child in property.ChildTransformations ?? [])
            CountGrouping(child, settings, ref nodes);
    }

    private static void ValidateAggregate(
        AggregateExpressionBase expression,
        SynaxODataOptions settings,
        ref int nodes)
    {
        switch (expression)
        {
            case AggregateExpression aggregate:
                Count(aggregate.Expression, settings, ref nodes);
                RequireMethod(aggregate, settings);
                break;

            case EntitySetAggregateExpression entitySet:
                foreach (var child in entitySet.Children)
                    ValidateAggregate(child, settings, ref nodes);
                break;

            default:
                throw new SynaxODataQueryException(
                    $"The $apply aggregate expression '{expression.AggregateKind}' is not supported.");
        }
    }

    private static void RequireMethod(AggregateExpression aggregate, SynaxODataOptions settings)
    {
        if (settings.AllowedAggregations.Count == 0)
            return;

        var method = MethodName(aggregate);

        if (!settings.AllowedAggregations.Contains(method))
            throw new SynaxODataQueryException(
                $"The aggregation method '{method}' is not allowed " +
                "(SynaxODataOptions.AllowedAggregations). Allowed: " +
                string.Join(", ", settings.AllowedAggregations.Order(StringComparer.OrdinalIgnoreCase)) + ".");
    }

    /// <summary>The method name as a client writes it in <c>$apply</c>.</summary>
    private static string MethodName(AggregateExpression aggregate) => aggregate.Method switch
    {
        AggregationMethod.Sum => "sum",
        AggregationMethod.Min => "min",
        AggregationMethod.Max => "max",
        AggregationMethod.Average => "average",
        AggregationMethod.CountDistinct => "countdistinct",
        AggregationMethod.VirtualPropertyCount => "$count",
        _ => aggregate.MethodDefinition?.MethodLabel ?? aggregate.Method.ToString().ToLowerInvariant()
    };

    private static void Count(QueryNode? node, SynaxODataOptions settings, ref int nodes)
    {
        nodes += QueryNodes.Count(node);

        if (nodes > settings.MaxNodeCount)
            throw new SynaxODataQueryException(
                $"The $apply expression is too complex: it exceeds the {settings.MaxNodeCount}-node " +
                "limit (SynaxODataOptions.MaxNodeCount).");
    }
}
