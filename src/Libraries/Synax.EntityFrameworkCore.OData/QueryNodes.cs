using Microsoft.OData.UriParser;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Tree helpers over the OData syntax nodes Synax has to inspect itself. The package's validators
/// cover <c>$filter</c> and <c>$orderby</c> but walk neither <c>$compute</c> nor <c>$apply</c>, so
/// the guardrails for those two (<see cref="ComputeGuardrails"/>, <see cref="ApplyGuardrails"/>)
/// share this walker rather than each carrying its own copy of the node taxonomy.
/// </summary>
internal static class QueryNodes
{
    /// <summary>The EDM name a node reads, or <c>null</c> when the node reads no property.</summary>
    public static string? PropertyName(QueryNode node) => node switch
    {
        SingleValuePropertyAccessNode access => access.Property.Name,
        CollectionPropertyAccessNode access => access.Property.Name,
        SingleComplexNode complex => complex.Property.Name,
        CollectionComplexNode complex => complex.Property.Name,
        SingleNavigationNode navigation => navigation.NavigationProperty.Name,
        CollectionNavigationNode navigation => navigation.NavigationProperty.Name,
        SingleValueOpenPropertyAccessNode open => open.Name,
        CollectionOpenPropertyAccessNode open => open.Name,
        _ => null
    };

    /// <summary>
    /// The child nodes of <paramref name="node"/>, or <c>null</c> when the node type is not one this
    /// walker knows — deliberately distinct from "no children" so a caller enforcing an allow-list
    /// can refuse a construct it is unable to inspect.
    /// </summary>
    public static IEnumerable<QueryNode>? Children(QueryNode node) => node switch
    {
        ConstantNode or CollectionConstantNode or SearchTermNode or ParameterAliasNode
            or ResourceRangeVariableReferenceNode or NonResourceRangeVariableReferenceNode => [],
        ConvertNode convert => [convert.Source],
        UnaryOperatorNode unary => [unary.Operand],
        BinaryOperatorNode binary => [binary.Left, binary.Right],
        InNode inNode => [inNode.Left, inNode.Right],
        CountNode count => [count.Source],
        SingleValueFunctionCallNode function => function.Parameters,
        SingleResourceFunctionCallNode function => function.Parameters,
        CollectionFunctionCallNode function => function.Parameters,
        CollectionResourceFunctionCallNode function => function.Parameters,
        NamedFunctionParameterNode parameter => [parameter.Value],
        SingleValuePropertyAccessNode access => [access.Source],
        CollectionPropertyAccessNode access => [access.Source],
        SingleComplexNode complex => [complex.Source],
        CollectionComplexNode complex => [complex.Source],
        SingleNavigationNode navigation => [navigation.Source],
        CollectionNavigationNode navigation => [navigation.Source],
        SingleValueOpenPropertyAccessNode open => [open.Source],
        CollectionOpenPropertyAccessNode open => [open.Source],
        SingleValueCastNode cast => [cast.Source],
        SingleResourceCastNode cast => [cast.Source],
        CollectionResourceCastNode cast => [cast.Source],
        AnyNode lambda => [lambda.Source, lambda.Body],
        AllNode lambda => [lambda.Source, lambda.Body],
        _ => null
    };

    /// <summary>
    /// The number of nodes in a tree, counting a node this walker does not recognize as one leaf.
    /// Used to bound client-supplied expressions the OData validators do not measure.
    /// </summary>
    public static int Count(QueryNode? node)
    {
        if (node is null)
            return 0;

        var total = 1;
        foreach (var child in Children(node) ?? [])
            total += Count(child);

        return total;
    }
}
