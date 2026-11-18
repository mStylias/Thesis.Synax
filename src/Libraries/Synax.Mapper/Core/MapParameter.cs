using System.Linq.Expressions;

namespace Synax.Mapper.Core;

/// <summary>
/// A value a projection needs per call rather than once at startup — the request's language, say.
/// Declared in a profile with <see cref="MapperProfile.Parameter{T}"/>, read through
/// <see cref="Value"/> inside a <c>For</c> expression, and supplied per projection with
/// <see cref="MapParameters"/>.
/// <para>
/// The projection replaces <see cref="Value"/> with the supplied value held in a closure, so EF Core
/// sends it as a SQL parameter and caches one query plan for every value.
/// </para>
/// </summary>
public sealed class MapParameter<T> : IMapParameter
{
    internal MapParameter(string name) => Name = name;

    /// <summary>The name a caller supplies the value under.</summary>
    public string Name { get; }

    Type IMapParameter.Type => typeof(T);

    /// <summary>
    /// The supplied value. Only meaningful inside a mapping expression: reading it anywhere else,
    /// or mapping in memory through a map that reads it, throws.
    /// </summary>
    public T Value => throw new InvalidOperationException(
        $"Map parameter '{Name}' has no value here. It can only be read inside a projection that " +
        "supplies it through MapParameters (ProjectTo, GetExpression or QueryDto with parameters); " +
        "in-memory Map calls cannot supply one.");

    Expression IMapParameter.CreateValueAccess(object? value)
    {
        if (value is null)
        {
            if (default(T) is not null)
                throw new ArgumentException(
                    $"Map parameter '{Name}' is a '{typeof(T).Name}', a value type, and was supplied null.");

            return Expression.Field(Expression.Constant(new MapParameterValue<T>(default!)), nameof(MapParameterValue<>.Value));
        }

        if (value is not T typed)
            throw new ArgumentException(
                $"Map parameter '{Name}' is a '{typeof(T).Name}' and was supplied a '{value.GetType().Name}'.");

        return Expression.Field(Expression.Constant(new MapParameterValue<T>(typed)), nameof(MapParameterValue<>.Value));
    }
}

/// <summary>Non-generic view of <see cref="MapParameter{T}"/>, so binding needs no reflection.</summary>
internal interface IMapParameter
{
    string Name { get; }

    Type Type { get; }

    /// <summary>A field read on a fresh holder — the closure shape EF Core turns into a SQL parameter.</summary>
    Expression CreateValueAccess(object? value);
}

/// <summary>
/// The supplied values of a projection's <see cref="MapParameter{T}"/>s, keyed by name. A map that
/// declares a parameter must be given every one it reads, and nothing it does not.
/// </summary>
public sealed class MapParameters() : Dictionary<string, object?>(StringComparer.Ordinal);

/// <summary>A field, not a property: EF Core parameterizes a member read on a constant.</summary>
internal sealed class MapParameterValue<T>(T value)
{
    public readonly T Value = value;
}
