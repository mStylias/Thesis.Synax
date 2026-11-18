using System.Linq.Expressions;
using Synax.Mapper.Internal;

namespace Synax.Mapper.Core;

/// <summary>
/// Base class for defining mapping configurations. Create a derived class and
/// call <see cref="CreateMap{TSource,TDest}"/> in the constructor for each
/// source-destination pair.
/// <para>
/// <b>Example:</b>
/// <code>
/// public class OrderProfile : MapperProfile
/// {
///     public OrderProfile()
///     {
///         CreateMap&lt;Order, OrderDto&gt;()
///             .For(d => d.Id, s => s.Id)
///             .For(d => d.Total, s => s.Lines.Sum(l => l.Price * l.Qty));
///     }
/// }
/// </code>
/// </para>
/// </summary>
public abstract class MapperProfile
{
    internal Dictionary<(Type Source, Type Dest), IMappingBuilder> Builders { get; } = [];

    internal Dictionary<(Type Source, Type Dest), LambdaExpression> Converters { get; } = [];

    /// <summary>
    /// Begins a mapping definition from <typeparamref name="TSource"/> to
    /// <typeparamref name="TDest"/>. Returns a fluent builder for configuring
    /// individual member bindings.
    /// </summary>
    protected MappingBuilder<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var key = (typeof(TSource), typeof(TDest));

        if (Builders.ContainsKey(key))
            throw new InvalidOperationException(
                $"A mapping from '{typeof(TSource).Name}' to '{typeof(TDest).Name}' " +
                $"is already defined in profile '{GetType().Name}'.");

        var builder = new MappingBuilder<TSource, TDest>();
        Builders[key] = builder;
        return builder;
    }

    /// <summary>
    /// Declares a value the profile's projections read per call, supplied with
    /// <see cref="MapParameters"/> under <paramref name="name"/>. Read its
    /// <see cref="MapParameter{T}.Value"/> inside a <c>For</c> expression; a projection through a map
    /// that reads it must supply it, or it throws.
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var languageId = Parameter&lt;Guid?&gt;("languageId");
    /// CreateMap&lt;Service, ServiceDto&gt;()
    ///     .For(d =&gt; d.Name, s =&gt; s.Translations
    ///         .Where(t =&gt; t.LanguageId == languageId.Value)
    ///         .Select(t =&gt; t.Name)
    ///         .FirstOrDefault());
    /// </code>
    /// </para>
    /// </summary>
    protected MapParameter<T> Parameter<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_parametersByName.TryGetValue(name, out var existing))
        {
            return existing as MapParameter<T> ?? throw new InvalidOperationException(
                $"Map parameter '{name}' is already declared as '{existing.Type.Name}' in profile '{GetType().Name}'.");
        }

        var parameter = new MapParameter<T>(name);
        _parametersByName[name] = parameter;
        return parameter;
    }

    private readonly Dictionary<string, IMapParameter> _parametersByName = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a global converter applied wherever a member of type
    /// <typeparamref name="TSource"/> is bound to a member of type
    /// <typeparamref name="TDest"/> — in explicit <c>For</c> bindings, AutoMap matches,
    /// and collection elements — without repeating the conversion per member.
    /// A registered converter takes precedence over every other binding strategy.
    /// <para>
    /// The conversion is an expression so it stays usable in IQueryable projection;
    /// keep it EF-translatable when the mapping is used with <c>ProjectTo</c>.
    /// For a <c>Nullable&lt;TSource&gt;</c> member the converter is lifted automatically
    /// with a null guard.
    /// </para>
    /// <para>
    /// <b>Example:</b><br/>
    /// <c>ConvertUsing&lt;DateTime, string&gt;(d =&gt; d.ToString("O"));</c>
    /// </para>
    /// </summary>
    protected void ConvertUsing<TSource, TDest>(Expression<Func<TSource, TDest>> conversion)
    {
        var key = (typeof(TSource), typeof(TDest));

        if (!Converters.TryAdd(key, conversion))
            throw new InvalidOperationException(
                $"A converter from '{typeof(TSource).Name}' to '{typeof(TDest).Name}' " +
                $"is already defined in profile '{GetType().Name}'.");
    }
}
