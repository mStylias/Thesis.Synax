using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;

namespace Synax.Mapper.Internal;

/// <summary>
/// Static helpers for dissecting and composing expression trees.
/// </summary>
internal static class ExpressionHelper
{
    /// <summary>
    /// Extracts the <see cref="MemberInfo"/> from a simple member-access lambda such as
    /// <c>d => d.PropertyName</c>. Handles implicit Convert nodes inserted by the compiler
    /// when the property type is implicitly convertible to the declared delegate return type.
    /// </summary>
    public static MemberInfo ExtractMember(LambdaExpression expression)
    {
        var body = expression.Body;

        // Unwrap Convert / ConvertChecked (compiler-inserted casts)
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            body = unary.Operand;

        if (body is MemberExpression member)
            return member.Member;

        throw new ArgumentException(
            $"Expression '{expression}' does not refer to a property or field. " +
            "Use a simple member access like d => d.MyProperty.",
            nameof(expression));
    }

    /// <summary>
    /// Wraps a <c>Select(...)</c> call with the appropriate materialization method
    /// based on the destination member's declared type. Supports arrays, lists, hash sets,
    /// read-only collections, and falls back to constructor-based materialization
    /// for types that accept <c>IEnumerable&lt;T&gt;</c>. Returns the
    /// <paramref name="selectCall"/> unchanged when the destination accepts
    /// <see cref="IEnumerable{T}"/> directly.
    /// </summary>
    public static Expression MaterializeIfNeeded(Expression selectCall, MemberInfo destinationMember, Type elementType)
    {
        var destType = destinationMember switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => throw new InvalidOperationException($"Unsupported member type: {destinationMember.MemberType}")
        };

        // -------- Array --------
        if (destType.IsArray)
        {
            return Expression.Call(
                typeof(Enumerable), nameof(Enumerable.ToArray),
                [elementType], selectCall);
        }

        if (!destType.IsGenericType)
            return selectCall;

        var genericDef = destType.GetGenericTypeDefinition();

        // -------- List<T> and list-compatible interfaces --------
        if (genericDef == typeof(List<>) ||
            genericDef == typeof(IList<>) ||
            genericDef == typeof(ICollection<>) ||
            genericDef == typeof(IReadOnlyList<>) ||
            genericDef == typeof(IReadOnlyCollection<>))
        {
            return Expression.Call(
                typeof(Enumerable), nameof(Enumerable.ToList),
                [elementType], selectCall);
        }

        // -------- HashSet<T> and set interfaces --------
        if (genericDef == typeof(HashSet<>) ||
            genericDef == typeof(ISet<>) ||
            genericDef == typeof(IReadOnlySet<>))
        {
            return Expression.Call(
                typeof(Enumerable), nameof(Enumerable.ToHashSet),
                [elementType], selectCall);
        }

        // -------- Dictionary<TKey, TValue> and dictionary interfaces --------
        // Only applicable when the element is KeyValuePair<TKey, TValue>
        if ((genericDef == typeof(Dictionary<,>) ||
             genericDef == typeof(IDictionary<,>) ||
             genericDef == typeof(IReadOnlyDictionary<,>)) &&
            elementType.IsGenericType &&
            elementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            var kvpArgs = elementType.GetGenericArguments();
            var keyType = kvpArgs[0];
            var valueType = kvpArgs[1];

            // Build lambda: kvp => kvp.Key
            var kvpParam = Expression.Parameter(elementType, "kvp");
            var keySelector = Expression.Lambda(
                Expression.Property(kvpParam, nameof(KeyValuePair<,>.Key)),
                kvpParam);

            // Build lambda: kvp => kvp.Value
            var valueSelector = Expression.Lambda(
                Expression.Property(kvpParam, nameof(KeyValuePair<,>.Value)),
                kvpParam);

            return Expression.Call(
                typeof(Enumerable), nameof(Enumerable.ToDictionary),
                [elementType, keyType, valueType],
                selectCall, keySelector, valueSelector);
        }

        // -------- Immutable collections --------
        // These use static factory methods: ImmutableList.CreateRange<T>(source)
        // Check by namespace + name since referencing System.Collections.Immutable
        // would add a hard dependency. The types are resolved at expression build time
        // so they must already be loaded.

        var fullName = genericDef.FullName;

        if (fullName is not null)
        {
            var factoryResult = TryBuildImmutableFactory(fullName, destType, elementType, selectCall);
            if (factoryResult is not null)
                return factoryResult;
        }

        // -------- Constructor fallback --------
        // If the concrete type has a constructor accepting IEnumerable<T>,
        // use it. Handles custom collection types like SortedSet<T>,
        // LinkedList<T>, ReadOnlyCollection<T> wrapper, etc.
        if (!destType.IsInterface && !destType.IsAbstract)
        {
            var enumerableOfElement = typeof(IEnumerable<>).MakeGenericType(elementType);
            var ctor = destType.GetConstructor([enumerableOfElement]);

            if (ctor is not null)
            {
                return Expression.New(ctor, selectCall);
            }
        }

        // IEnumerable<T> or compatible — return as-is (deferred execution)
        return selectCall;
    }

    /// <summary>
    /// Attempts to build a factory call for System.Collections.Immutable types.
    /// Returns <c>null</c> if the type is not a known immutable collection or
    /// if the factory type is not loaded.
    /// </summary>
    private static Expression? TryBuildImmutableFactory(
        string genericDefFullName, Type destType, Type elementType, Expression selectCall)
    {
        // Map from generic collection type name → static factory class name + method
        // ImmutableList<T>      → ImmutableList.CreateRange<T>
        // ImmutableArray<T>     → ImmutableArray.CreateRange<T>
        // ImmutableHashSet<T>   → ImmutableHashSet.CreateRange<T>
        // ImmutableSortedSet<T> → ImmutableSortedSet.CreateRange<T>
        // ImmutableQueue<T>     → ImmutableQueue.CreateRange<T>
        // ImmutableStack<T>     → ImmutableStack.CreateRange<T>
        // Also handle the interfaces: IImmutableList<T>, IImmutableSet<T>, etc.

        const string ns = "System.Collections.Immutable.";

        string? factoryTypeName = genericDefFullName switch
        {
            $"{ns}ImmutableList`1" or $"{ns}IImmutableList`1"
                => $"{ns}ImmutableList",
            $"{ns}ImmutableArray`1"
                => $"{ns}ImmutableArray",
            $"{ns}ImmutableHashSet`1" or $"{ns}IImmutableSet`1"
                => $"{ns}ImmutableHashSet",
            $"{ns}ImmutableSortedSet`1"
                => $"{ns}ImmutableSortedSet",
            $"{ns}ImmutableQueue`1" or $"{ns}IImmutableQueue`1"
                => $"{ns}ImmutableQueue",
            $"{ns}ImmutableStack`1" or $"{ns}IImmutableStack`1"
                => $"{ns}ImmutableStack",
            _ => null
        };

        if (factoryTypeName is null)
            return null;

        // Try to find the non-generic factory class in loaded assemblies
        var factoryType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(factoryTypeName))
            .FirstOrDefault(t => t is not null);

        if (factoryType is null)
            return null;

        // All immutable factory classes expose CreateRange<T>(IEnumerable<T>)
        var createRange = factoryType.GetMethod(
            "CreateRange",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(IEnumerable<>).MakeGenericType(elementType)],
            null);

        // Fallback: find the generic method definition and make it concrete
        createRange ??= factoryType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "CreateRange" && m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 1)
            .Where(m => m.GetParameters().Length == 1)
            .Select(m => m.MakeGenericMethod(elementType))
            .FirstOrDefault();

        if (createRange is null)
            return null;

        return Expression.Call(createRange, selectCall);
    }

    // -------- Conversions --------

    /// <summary>
    /// Standard implicit numeric conversions (C# spec §10.2.3), keyed by source type.
    /// </summary>
    private static readonly FrozenDictionary<Type, Type[]> ImplicitNumericConversions =
        new Dictionary<Type, Type[]>
        {
            [typeof(sbyte)] = [typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
            [typeof(byte)] = [typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
            [typeof(short)] = [typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
            [typeof(ushort)] = [typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
            [typeof(int)] = [typeof(long), typeof(float), typeof(double), typeof(decimal)],
            [typeof(uint)] = [typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
            [typeof(long)] = [typeof(float), typeof(double), typeof(decimal)],
            [typeof(ulong)] = [typeof(float), typeof(double), typeof(decimal)],
            [typeof(char)] = [typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
            [typeof(float)] = [typeof(double)]
        }.ToFrozenDictionary();

    /// <summary>
    /// Attempts a conversion that is safe to apply <b>automatically</b> (lossless and
    /// EF-translatable): implicit numeric widening (<c>int</c> → <c>long</c>) including
    /// lifted forms (<c>int?</c> → <c>long?</c>), enum → enum by value,
    /// enum → string via <c>ToString()</c>, and user-defined implicit operators.
    /// Narrowing and other explicit conversions are never applied — returns <c>null</c>
    /// so callers can decide.
    /// </summary>
    public static Expression? TryAutoConvert(Expression access, Type destType)
    {
        var srcType = access.Type;
        var srcValue = Nullable.GetUnderlyingType(srcType) ?? srcType;
        var destValue = Nullable.GetUnderlyingType(destType) ?? destType;
        var srcIsNullable = srcValue != srcType;
        var destIsNullable = destValue != destType;

        // ---- enum → string (Status.Active → "Active") ----
        if (destType == typeof(string) && srcValue.IsEnum)
        {
            var toString = srcValue.GetMethod(nameof(ToString), Type.EmptyTypes)!;

            if (!srcIsNullable)
                return Expression.Call(access, toString);

            // nullable enum: src == null ? null : src.Value.ToString()
            return Expression.Condition(
                Expression.Equal(access, Expression.Constant(null, srcType)),
                Expression.Constant(null, typeof(string)),
                Expression.Call(Expression.Property(access, nameof(Nullable<int>.Value)), toString));
        }

        // A nullable value never implicitly becomes non-nullable
        if (srcIsNullable && !destIsNullable)
            return null;

        // ---- enum → enum (by value) and implicit numeric widening ----
        if ((srcValue.IsEnum && destValue.IsEnum)
            || (ImplicitNumericConversions.TryGetValue(srcValue, out var widenings)
                && widenings.Contains(destValue)))
        {
            return Expression.Convert(access, destType);
        }

        // ---- user-defined implicit operator ----
        var op = FindImplicitOperator(srcType, destType);
        return op is not null ? Expression.Convert(access, destType, op) : null;
    }

    /// <summary>
    /// Attempts <b>any</b> standard or user-defined conversion via
    /// <see cref="Expression.Convert(Expression, Type)"/>, including narrowing and
    /// explicit operators. Returns <c>null</c> when no conversion exists between the types.
    /// </summary>
    public static Expression? TryConvert(Expression access, Type destType)
    {
        try
        {
            return Expression.Convert(access, destType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to bind via a registered converter. Matches the exact type pair first;
    /// for a <c>Nullable&lt;T&gt;</c> source it also tries the underlying pair and lifts
    /// the converter with a null guard (<c>src == null ? default : convert(src.Value)</c>),
    /// provided the destination can hold <c>null</c>. The converter body is inlined
    /// (no <c>Invoke</c> nodes), so the result stays EF-translatable when the converter is.
    /// </summary>
    public static Expression? TryApplyConverter(
        Expression access, Type destType, Func<Type, Type, LambdaExpression?> resolveConverter)
    {
        // ---- Exact pair ----
        var converter = resolveConverter(access.Type, destType);
        if (converter is not null)
            return ParameterReplacer.Replace(converter.Body, converter.Parameters[0], access);

        // ---- Lifted pair: TSrc? -> TDest via converter TSrc -> TDestValue ----
        var srcValue = Nullable.GetUnderlyingType(access.Type);
        if (srcValue is null)
            return null;

        var destValue = Nullable.GetUnderlyingType(destType) ?? destType;
        var destAcceptsNull = !destType.IsValueType || destValue != destType;
        if (!destAcceptsNull)
            return null;

        converter = resolveConverter(srcValue, destValue);
        if (converter is null)
            return null;

        var valueAccess = Expression.Property(access, nameof(Nullable<int>.Value));
        var inlined = ParameterReplacer.Replace(converter.Body, converter.Parameters[0], valueAccess);
        if (inlined.Type != destType)
            inlined = Expression.Convert(inlined, destType);

        return Expression.Condition(
            Expression.Equal(access, Expression.Constant(null, access.Type)),
            Expression.Default(destType),
            inlined);
    }

    /// <summary>
    /// Finds a user-defined implicit conversion operator declared on either the source
    /// or the destination type.
    /// </summary>
    private static MethodInfo? FindImplicitOperator(Type srcType, Type destType)
    {
        return srcType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Concat(destType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m =>
                m.Name == "op_Implicit"
                && m.ReturnType == destType
                && m.GetParameters() is [var parameter]
                && parameter.ParameterType.IsAssignableFrom(srcType));
    }
}