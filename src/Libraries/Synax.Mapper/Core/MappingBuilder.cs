using System.Linq.Expressions;
using System.Reflection;
using Synax.Mapper.Internal;

namespace Synax.Mapper.Core;

/// <summary>
/// Fluent builder for defining how properties of <typeparamref name="TSource"/>
/// map to properties of <typeparamref name="TDest"/>. All mappings are captured
/// as expression trees, enabling both in-memory compilation and IQueryable projection.
/// </summary>
/// <typeparam name="TSource">The source (entity / domain) type.</typeparam>
/// <typeparam name="TDest">The destination (DTO / view-model) type.</typeparam>
public sealed class MappingBuilder<TSource, TDest> : IMappingBuilder
{
    // -------- Internal binding storage --------
    // Each entry stores (destinationMember, sourceLambda) where sourceLambda is
    // relative to its own parameter — rebound to the shared source parameter and
    // classified (direct / collection / nested / convert) during Build().

    private readonly List<(MemberInfo Dest, LambdaExpression Source)> _bindings = [];
    private readonly HashSet<string> _ignoredMembers = new(StringComparer.Ordinal);

    private bool _autoMap = true;

    internal MappingBuilder() { }

    // -------- Member mapping --------

    /// <summary>
    /// Maps a destination member from an arbitrary source expression. The binding
    /// strategy is selected automatically at configuration build time from the
    /// expression's type and the destination member's type:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Assignable types</b> — bound directly. Value-type conversions such as
    ///     nullable lifting (<c>int</c> → <c>int?</c>) and boxing are applied automatically.
    ///   </item>
    ///   <item>
    ///     <b>Collections</b> — when both sides implement <c>IEnumerable&lt;T&gt;</c> and a
    ///     mapping is registered for the element types, an <c>Enumerable.Select</c>
    ///     projection is generated and materialized to the destination collection type
    ///     (<c>List&lt;T&gt;</c>, <c>T[]</c>, <c>HashSet&lt;T&gt;</c>, ...).
    ///   </item>
    ///   <item>
    ///     <b>Nested objects</b> — when a mapping is registered for the member types, it is
    ///     inlined into the parent expression, producing a single flat <c>MemberInit</c>
    ///     tree that EF Core can fully translate. A null guard
    ///     (<c>source.Nav == null ? null : new Dto { ... }</c>) is emitted unless the
    ///     accessed member is declared non-nullable.
    ///   </item>
    ///   <item>
    ///     <b>Convertible types</b> — bound with a cast when a standard or user-defined
    ///     conversion exists (e.g. <c>int</c> → <c>long</c>), or via <c>ToString()</c>
    ///     for enum → string. Explicit bindings also accept narrowing conversions.
    ///   </item>
    /// </list>
    /// When no strategy applies, configuration fails at build time with a descriptive error.
    /// <para>
    /// <b>Examples:</b><br/>
    /// <c>.For(d => d.FullName, s => s.First + " " + s.Last)</c><br/>
    /// <c>.For(d => d.Address, s => s.ShippingAddress)</c> — inlines the registered Address mapping<br/>
    /// <c>.For(d => d.Items, s => s.OrderLines)</c> — projects each element via its registered mapping
    /// </para>
    /// </summary>
    public MappingBuilder<TSource, TDest> For<TDestMember, TSrcMember>(
        Expression<Func<TDest, TDestMember>> destination,
        Expression<Func<TSource, TSrcMember>> source)
    {
        var member = ExpressionHelper.ExtractMember(destination);
        _bindings.Add((member, source));
        return this;
    }

    // -------- Ignore --------

    /// <summary>
    /// Excludes a destination member from mapping. Ignored members are left at their
    /// default value and are also excluded from <see cref="AutoMap"/> matching.
    /// </summary>
    public MappingBuilder<TSource, TDest> Ignore<TMember>(
        Expression<Func<TDest, TMember>> destination)
    {
        var member = ExpressionHelper.ExtractMember(destination);
        _ignoredMembers.Add(member.Name);
        return this;
    }

    // -------- AutoMap --------

    /// <summary>
    /// After all explicit mappings, automatically binds remaining destination properties
    /// to same-named source properties. Handles four cases:
    /// <list type="bullet">
    ///   <item>Directly assignable types — bound with a simple property access.</item>
    ///   <item>
    ///     Nested complex types — if a mapping is registered for the source → destination
    ///     property types, the nested mapping is inlined with a null-safety check.
    ///   </item>
    ///   <item>
    ///     Collection properties — if both sides implement <c>IEnumerable&lt;T&gt;</c>
    ///     and a mapping is registered for the element types, a
    ///     <c>Select</c> + materialization expression is generated.
    ///   </item>
    ///   <item>
    ///     Convertible types — implicit numeric widening (<c>int</c> → <c>long</c>),
    ///     enum → enum, enum → string, and user-defined implicit operators are applied
    ///     automatically. Narrowing and other explicit conversions never are.
    ///   </item>
    /// </list>
    /// Properties with no matching source property, incompatible types, or no registered
    /// mapping are skipped (left at their default value). Use
    /// <see cref="MapperConfiguration.AssertConfigurationIsValid"/> to detect skipped
    /// members instead of discovering them at runtime.
    /// </summary>
    public MappingBuilder<TSource, TDest> AutoMap(bool enabled = true)
    {
        _autoMap = enabled;
        return this;
    }

    // -------- Build --------

    /// <summary>
    /// Composes all registered bindings into the construction expression
    /// (<c>Func&lt;TSource, TDest&gt;</c>) and the update expression
    /// (<c>Action&lt;TSource, TDest&gt;</c>), and reports the destination members
    /// left unbound.
    /// </summary>
    MappingBuildResult IMappingBuilder.Build(MappingBuildContext context)
    {
        var srcParam = Expression.Parameter(typeof(TSource), "src");
        var bindings = new List<(MemberInfo Dest, Expression Value)>();
        var mappedNames = new HashSet<string>(StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();

        // -------- Explicit bindings --------
        foreach (var (dest, sourceLambda) in _bindings)
        {
            var access = ParameterReplacer.Replace(
                sourceLambda.Body, sourceLambda.Parameters[0], srcParam);

            bindings.Add((dest, BindMember(dest, access, context, nullability)));
            mappedNames.Add(dest.Name);
        }

        // -------- AutoMap remaining same-name properties --------
        if (_autoMap)
        {
            var allMapped = mappedNames.Union(_ignoredMembers).ToHashSet(StringComparer.Ordinal);

            foreach (var destProp in typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite || allMapped.Contains(destProp.Name))
                    continue;

                var srcProp = typeof(TSource).GetProperty(
                    destProp.Name, BindingFlags.Public | BindingFlags.Instance);

                if (srcProp is null || !srcProp.CanRead)
                    continue;

                var srcPropType = srcProp.PropertyType;
                var destPropType = destProp.PropertyType;
                var accessExpr = Expression.Property(srcParam, srcProp);

                // ---- Case 0: Registered converter (always wins) ----
                if (ExpressionHelper.TryApplyConverter(
                        accessExpr, destPropType, context.ResolveConverter) is { } viaConverter)
                {
                    bindings.Add((destProp, viaConverter));
                    continue;
                }

                // ---- Case 1: Directly assignable (primitives, strings, same-type refs) ----
                if (destPropType.IsAssignableFrom(srcPropType))
                {
                    bindings.Add((destProp, BindAssignable(accessExpr, destPropType)));
                    continue;
                }

                // ---- Case 2: Collection mapping ----
                // Check collections before nested (collections are also classes)
                var srcElem = GetEnumerableElementType(srcPropType);
                var destElem = GetEnumerableElementType(destPropType);

                if (srcElem is not null && destElem is not null)
                {
                    var elemMapping = ResolveElementMapping(srcElem, destElem, context);
                    if (elemMapping is not null)
                    {
                        bindings.Add((destProp, BuildCollectionExpression(
                            accessExpr, srcElem, destElem, elemMapping, destProp)));
                    }
                    continue;
                }

                // ---- Case 3: Nested complex type mapping ----
                if (srcPropType.IsClass && destPropType.IsClass
                    && srcPropType != typeof(string) && destPropType != typeof(string))
                {
                    var nestedMap = context.ResolveMap(srcPropType, destPropType);
                    if (nestedMap is not null)
                    {
                        // Always null-safe: AutoMap cannot know whether the navigation is loaded
                        bindings.Add((destProp, BuildNestedExpression(
                            accessExpr, nestedMap, destPropType, nullGuard: true)));
                        continue;
                    }
                }

                // ---- Case 4: Safe automatic conversion (widening, enum → enum, enum → string) ----
                if (ExpressionHelper.TryAutoConvert(accessExpr, destPropType) is { } converted)
                {
                    bindings.Add((destProp, converted));
                }
            }
        }

        var body = BuildDestination(bindings);
        var lambda = Expression.Lambda<Func<TSource, TDest>>(body, srcParam);

        // Report public writable destination properties that no binding covers —
        // surfaced through TypeMap.UnmappedMembers and AssertConfigurationIsValid.
        var boundNames = bindings.Select(b => b.Dest.Name).ToHashSet(StringComparer.Ordinal);
        var unmappedMembers = typeof(TDest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && !boundNames.Contains(p.Name) && !_ignoredMembers.Contains(p.Name))
            .Select(p => p.Name)
            .ToArray();

        return new MappingBuildResult(lambda, BuildUpdateExpression(bindings, srcParam), unmappedMembers);
    }

    /// <summary>
    /// Composes the same bindings into an <c>Action&lt;TSource, TDest&gt;</c> that
    /// assigns each value onto an existing destination instance — the update path used
    /// by <c>IMapper.Map(source, destination)</c>. Only writable members are assigned;
    /// nested objects and collections are replaced, not merged.
    /// </summary>
    private static LambdaExpression BuildUpdateExpression(
        List<(MemberInfo Dest, Expression Value)> bindings, ParameterExpression srcParam)
    {
        var destParam = Expression.Parameter(typeof(TDest), "dest");

        var assignments = bindings
            .Where(b => IsWritable(b.Dest))
            .Select(b => (Expression)Expression.Assign(
                Expression.MakeMemberAccess(destParam, b.Dest), b.Value))
            .ToList();

        return Expression.Lambda<Action<TSource, TDest>>(
            assignments.Count > 0 ? Expression.Block(assignments) : Expression.Empty(),
            srcParam, destParam);
    }

    // -------- Binding classification --------

    /// <summary>
    /// Selects the binding strategy for an explicit <see cref="For{TDestMember,TSrcMember}"/>
    /// based on the source expression's type and the destination member's type:
    /// registered converter, direct assignment, element-mapped collection, inlined nested
    /// mapping, or conversion.
    /// Throws when none applies, so misconfigurations fail at configuration build time.
    /// </summary>
    private static Expression BindMember(
        MemberInfo dest,
        Expression access,
        MappingBuildContext context,
        NullabilityInfoContext nullability)
    {
        var destType = GetMemberType(dest);
        var srcType = access.Type;

        // ---- Registered converter (always wins) ----
        if (ExpressionHelper.TryApplyConverter(access, destType, context.ResolveConverter) is { } viaConverter)
            return viaConverter;

        // ---- Directly assignable ----
        if (destType.IsAssignableFrom(srcType))
            return BindAssignable(access, destType);

        // ---- Collection with a registered element mapping or converter ----
        var srcElem = GetEnumerableElementType(srcType);
        var destElem = GetEnumerableElementType(destType);

        if (srcElem is not null && destElem is not null
            && ResolveElementMapping(srcElem, destElem, context) is { } elemMapping)
        {
            return BuildCollectionExpression(access, srcElem, destElem, elemMapping, dest);
        }

        // ---- Nested object with a registered mapping ----
        if (srcType.IsClass && destType.IsClass
            && srcType != typeof(string) && destType != typeof(string)
            && context.ResolveMap(srcType, destType) is { } nestedMap)
        {
            return BuildNestedExpression(access, nestedMap, destType, NeedsNullGuard(access, nullability));
        }

        // ---- Conversion: enum -> string and the like first, then any standard /
        // user-defined conversion (explicit binding opts into narrowing casts) ----
        if ((ExpressionHelper.TryAutoConvert(access, destType)
             ?? ExpressionHelper.TryConvert(access, destType)) is { } converted)
        {
            return converted;
        }

        var hint = srcElem is not null && destElem is not null
            ? $"Register a mapping from '{srcElem.Name}' to '{destElem.Name}' to project the collection elements."
            : $"Register a mapping from '{srcType.Name}' to '{destType.Name}' or bind an expression of a compatible type.";

        throw new InvalidOperationException(
            $"Cannot bind '{typeof(TDest).Name}.{dest.Name}' ({destType.Name}) from an expression " +
            $"of type '{srcType.Name}': the types are not assignable, no mapping is registered for " +
            $"them, and no conversion exists. {hint}");
    }

    /// <summary>
    /// Binds an expression to an assignable destination type. Reflection assignability
    /// includes boxing and nullable-lifting conversions (<c>int</c> → <c>int?</c>,
    /// <c>int</c> → <c>object</c>) that expression trees only accept through an
    /// explicit Convert node.
    /// </summary>
    private static Expression BindAssignable(Expression access, Type destType)
        => destType != access.Type && access.Type.IsValueType
            ? Expression.Convert(access, destType)
            : access;

    /// <summary>
    /// Resolves how collection elements are projected: a registered converter for the
    /// element types wins, otherwise the registered element mapping is inlined.
    /// </summary>
    private static LambdaExpression? ResolveElementMapping(
        Type srcElem, Type destElem, MappingBuildContext context)
    {
        var elemParam = Expression.Parameter(srcElem, "e");
        if (ExpressionHelper.TryApplyConverter(elemParam, destElem, context.ResolveConverter) is { } converted)
            return Expression.Lambda(converted, elemParam);

        return context.ResolveMap(srcElem, destElem)?.Expression;
    }

    /// <summary>
    /// Builds <c>access.Select(elementMapping)</c> materialized to the destination
    /// member's declared collection type.
    /// </summary>
    private static Expression BuildCollectionExpression(
        Expression access, Type srcElem, Type destElem, LambdaExpression elementMapping, MemberInfo dest)
    {
        var selectCall = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Select),
            [srcElem, destElem], access, elementMapping);

        return ExpressionHelper.MaterializeIfNeeded(selectCall, dest, destElem);
    }

    /// <summary>
    /// Inlines a registered nested mapping by replacing its parameter with the access
    /// expression. With <paramref name="nullGuard"/> the result is wrapped in
    /// <c>access == null ? null : new Dto { ... }</c>, which EF Core translates
    /// correctly (CASE WHEN / LEFT JOIN) and which protects in-memory mapping from
    /// <see cref="NullReferenceException"/>.
    /// </summary>
    private static Expression BuildNestedExpression(
        Expression access, TypeMap nestedMap, Type destType, bool nullGuard)
    {
        var nestedExpr = nestedMap.Expression;
        var inlined = ParameterReplacer.Replace(nestedExpr.Body, nestedExpr.Parameters[0], access);

        if (!nullGuard)
            return inlined;

        return Expression.Condition(
            Expression.Equal(access, Expression.Constant(null, access.Type)),
            Expression.Constant(null, destType),
            inlined);
    }

    /// <summary>
    /// Determines whether an inlined nested mapping needs a null guard. Simple member
    /// accesses declared non-nullable (NRT) skip the guard, keeping the expression —
    /// and the translated SQL — minimal. Computed expressions and members declared
    /// nullable (or without annotations) are guarded defensively.
    /// </summary>
    private static bool NeedsNullGuard(Expression access, NullabilityInfoContext nullability)
    {
        if (access is not MemberExpression memberAccess)
            return true;

        var info = memberAccess.Member switch
        {
            PropertyInfo p => nullability.Create(p),
            FieldInfo f => nullability.Create(f),
            _ => null
        };

        return info?.ReadState != NullabilityState.NotNull;
    }

    // -------- Destination construction --------

    /// <summary>
    /// Composes the resolved member bindings into a destination-creating expression.
    /// <list type="bullet">
    ///   <item>
    ///     Types with a parameterless constructor (classes, structs, init-only records
    ///     declared without positional parameters) are built with a
    ///     <c>new TDest() { Member = value, ... }</c> member initializer.
    ///   </item>
    ///   <item>
    ///     Types <b>without</b> a parameterless constructor (positional records and other
    ///     immutable types) are built by routing each binding to the matching constructor
    ///     parameter by name — <c>new TDest(value, value, ...)</c>. Any remaining settable
    ///     members are then applied through an object initializer.
    ///   </item>
    /// </list>
    /// Both forms produce a <c>New</c> / <c>MemberInit</c> tree that EF Core can translate to SQL.
    /// </summary>
    private static Expression BuildDestination(List<(MemberInfo Dest, Expression Value)> bindings)
    {
        var destType = typeof(TDest);

        // -------- Member-init path (parameterless constructor available) --------
        if (destType.GetConstructor(Type.EmptyTypes) is { } parameterlessCtor)
        {
            var memberBindings = bindings.Select(b => (MemberBinding)Expression.Bind(b.Dest, b.Value));
            return Expression.MemberInit(Expression.New(parameterlessCtor), memberBindings);
        }

        // -------- Constructor path (records / immutable types) --------
        var ctor = SelectConstructor(destType);

        var valueByName = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dest, value) in bindings)
            valueByName[dest.Name] = value;

        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var arguments = new List<Expression>();

        foreach (var parameter in ctor.GetParameters())
        {
            if (parameter.Name is not null && valueByName.TryGetValue(parameter.Name, out var value))
            {
                arguments.Add(Coerce(value, parameter.ParameterType));
                consumed.Add(parameter.Name);
            }
            else
            {
                // Constructor parameter has no corresponding binding (ignored or unmapped) —
                // fall back to the parameter's default value.
                arguments.Add(Expression.Default(parameter.ParameterType));
            }
        }

        var newExpr = NewNamingItsMembers(ctor, arguments);

        // Bindings not absorbed by the constructor are applied through an object initializer.
        // Covers settable / init-only members declared in addition to the constructor parameters.
        var remaining = bindings
            .Where(b => !consumed.Contains(b.Dest.Name) && IsWritable(b.Dest))
            .Select(b => (MemberBinding)Expression.Bind(b.Dest, b.Value))
            .ToList();

        return remaining.Count > 0 ? Expression.MemberInit(newExpr, remaining) : newExpr;
    }

    /// <summary>
    /// Builds the constructor call, naming the member each argument initializes.
    /// </summary>
    /// <remarks>
    /// The name is what makes the projection <b>composable</b>. A consumer that filters or
    /// orders a projected queryable produces <c>new TDest(...).Member</c>, and it can only
    /// simplify that back to the argument bound to <c>Member</c> by reading
    /// <see cref="NewExpression.Members"/>. EF Core's projection simplification does exactly
    /// this. Without the names it leaves the whole constructor call inside the ORDER BY or
    /// WHERE and fails to translate the query — fatally so when a member maps to a subquery,
    /// which no provider can put there.
    /// <para>
    /// The compiler populates <c>Members</c> for anonymous types, which is why those compose
    /// and a hand-built <c>Expression.New(ctor, args)</c> does not.
    /// </para>
    /// <para>
    /// All or nothing: <see cref="Expression.New(ConstructorInfo, IEnumerable{Expression},
    /// IEnumerable{MemberInfo})"/> rejects a partial list, so a constructor parameter that
    /// surfaces no readable property of its own type falls back to the unnamed form rather
    /// than throwing.
    /// </para>
    /// </remarks>
    private static NewExpression NewNamingItsMembers(
        ConstructorInfo ctor,
        IReadOnlyList<Expression> arguments)
    {
        var parameters = ctor.GetParameters();
        var members = new MemberInfo[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            if (MatchingProperty(ctor.DeclaringType, parameters[i]) is not { } property)
                return Expression.New(ctor, arguments);

            members[i] = property;
        }

        return Expression.New(ctor, arguments, members);
    }

    /// <summary>
    /// The readable property a constructor parameter initializes, or <c>null</c> when there is
    /// no unambiguous one of the same type. Matched by name, as the record's own primary
    /// constructor does.
    /// </summary>
    private static PropertyInfo? MatchingProperty(Type? destType, ParameterInfo parameter)
    {
        if (destType is null || parameter.Name is null)
            return null;

        // Scanned rather than GetProperty(name, IgnoreCase): a type carrying two properties
        // that differ only by case makes that overload throw, and an ambiguous name is
        // precisely a case to decline instead of guess.
        PropertyInfo? match = null;
        foreach (var property in destType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (match is not null)
                return null;

            match = property;
        }

        return match is { CanRead: true } && match.PropertyType == parameter.ParameterType
            ? match
            : null;
    }

    /// <summary>
    /// Selects the constructor used to build a destination type that lacks a parameterless
    /// constructor. Picks the public instance constructor with the most parameters, which for
    /// positional records is the primary constructor (the copy constructor is non-public).
    /// </summary>
    private static ConstructorInfo SelectConstructor(Type destType)
    {
        var ctor = destType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        return ctor ?? throw new InvalidOperationException(
            $"Type '{destType.Name}' has no parameterless constructor and no accessible " +
            "constructor to map into. Add a public constructor or a parameterless constructor.");
    }

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="targetType"/> only when the
    /// expression's type differs, avoiding redundant <c>Convert</c> nodes that could hinder
    /// query translation.
    /// </summary>
    private static Expression Coerce(Expression value, Type targetType)
        => value.Type == targetType ? value : Expression.Convert(value, targetType);

    /// <summary>
    /// Indicates whether a member can be assigned through an object initializer
    /// (settable or init-only property, non-readonly field).
    /// </summary>
    private static bool IsWritable(MemberInfo member) => member switch
    {
        PropertyInfo p => p.CanWrite,
        FieldInfo f => f is { IsInitOnly: false, IsLiteral: false },
        _ => false
    };

    // -------- Helpers --------

    /// <summary>
    /// Returns the declared type of a property or field member.
    /// </summary>
    private static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        _ => throw new InvalidOperationException($"Unsupported member type: {member.MemberType}")
    };

    /// <summary>
    /// Extracts the element type <c>T</c> from a type that implements
    /// <c>IEnumerable&lt;T&gt;</c>. Returns <c>null</c> for <see cref="string"/>
    /// (which implements <c>IEnumerable&lt;char&gt;</c>) and for types that
    /// do not implement the generic enumerable interface.
    /// </summary>
    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
            return null;

        return type.GetInterfaces()
            .Concat(type.IsInterface && type.IsGenericType
                    && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? [type] : [])
            .Where(i => i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }
}
