using System.Linq.Expressions;
using System.Reflection;
using Synax.Mapper.Core;

namespace Synax.Mapper.Internal;

/// <summary>
/// Finds the <see cref="MapParameter{T}"/>s a mapping expression reads and swaps each for a supplied
/// value. A profile's <c>For</c> lambda reaches its parameter through the constructor's closure, so the
/// read is first pinned to the parameter instance itself (<see cref="Normalize"/>) — which is also what
/// lets a nested map, inlined into its parent, carry its parameters along.
/// </summary>
internal static class MapParameterBinder
{
    /// <summary>Rewrites every <c>closure.parameter.Value</c> into <c>Constant(parameter).Value</c>.</summary>
    public static Expression Normalize(Expression expression) => new Normalizer().Visit(expression);

    /// <summary>The parameters a normalized expression reads, by name.</summary>
    /// <exception cref="InvalidOperationException">One name is declared with two types.</exception>
    public static IReadOnlyDictionary<string, IMapParameter> Collect(Expression expression, Type sourceType, Type destType)
    {
        var collector = new Collector();
        collector.Visit(expression);

        var parametersByName = new Dictionary<string, IMapParameter>(StringComparer.Ordinal);
        foreach (var parameter in collector.Parameters)
        {
            if (!parametersByName.TryAdd(parameter.Name, parameter)
                && parametersByName[parameter.Name].Type != parameter.Type)
            {
                throw new InvalidOperationException(
                    $"The mapping '{sourceType.Name} → {destType.Name}' reads map parameter '{parameter.Name}' " +
                    $"as both '{parametersByName[parameter.Name].Type.Name}' and '{parameter.Type.Name}'. " +
                    "A name must have one type across every map inlined into it.");
            }
        }

        return parametersByName;
    }

    /// <summary>
    /// Replaces each parameter read with its supplied value. Every declared parameter must be
    /// supplied and nothing else may be, so a missing value or a misspelt name fails here rather
    /// than binding <c>null</c>.
    /// </summary>
    public static LambdaExpression Bind(
        LambdaExpression expression,
        IReadOnlyDictionary<string, IMapParameter> declared,
        IReadOnlyDictionary<string, object?>? supplied,
        Type sourceType,
        Type destType)
    {
        supplied ??= EmptyValues;

        var missing = declared.Keys.Where(name => !supplied.ContainsKey(name)).Order().ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The mapping '{sourceType.Name} → {destType.Name}' reads map parameter(s) " +
                $"{string.Join(", ", missing.Select(name => $"'{name}'"))}, which were not supplied. " +
                "Pass them through MapParameters.");

        var unknown = supplied.Keys.Where(name => !declared.ContainsKey(name)).Order().ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"The mapping '{sourceType.Name} → {destType.Name}' declares no map parameter(s) " +
                $"{string.Join(", ", unknown.Select(name => $"'{name}'"))}.");

        if (declared.Count == 0)
            return expression;

        var valueAccessByName = declared.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.CreateValueAccess(supplied[pair.Key]),
            StringComparer.Ordinal);

        return (LambdaExpression)new Binder(valueAccessByName).Visit(expression);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues = new Dictionary<string, object?>();

    private static bool IsValueRead(MemberExpression node) =>
        node.Member is PropertyInfo { Name: nameof(MapParameter<>.Value) } property
        && property.DeclaringType is { IsGenericType: true } declaringType
        && declaringType.GetGenericTypeDefinition() == typeof(MapParameter<>);

    private sealed class Normalizer : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (!IsValueRead(node) || node.Expression is ConstantExpression)
                return base.VisitMember(node);

            var parameter = Evaluate(node.Expression!);
            return Expression.Property(Expression.Constant(parameter, node.Expression!.Type), (PropertyInfo)node.Member);
        }

        /// <summary>A closure read is a chain of field reads ending at a constant; anything else is compiled.</summary>
        private static object Evaluate(Expression expression)
        {
            var value = expression switch
            {
                ConstantExpression constant => constant.Value,
                MemberExpression { Member: FieldInfo field, Expression: var owner } =>
                    field.GetValue(owner is null ? null : Evaluate(owner)),
                MemberExpression { Member: PropertyInfo property, Expression: var owner } =>
                    property.GetValue(owner is null ? null : Evaluate(owner)),
                _ => Expression.Lambda(expression).Compile().DynamicInvoke()
            };

            return value ?? throw new InvalidOperationException(
                $"A mapping reads '.Value' on a map parameter that is null ({expression}).");
        }
    }

    private sealed class Collector : ExpressionVisitor
    {
        public List<IMapParameter> Parameters { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            if (IsValueRead(node) && node.Expression is ConstantExpression { Value: IMapParameter parameter })
                Parameters.Add(parameter);

            return base.VisitMember(node);
        }
    }

    private sealed class Binder(IReadOnlyDictionary<string, Expression> valueAccessByName) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node) =>
            IsValueRead(node) && node.Expression is ConstantExpression { Value: IMapParameter parameter }
                ? valueAccessByName[parameter.Name]
                : base.VisitMember(node);
    }
}
