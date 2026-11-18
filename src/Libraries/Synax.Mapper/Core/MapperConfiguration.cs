using System.Collections.Frozen;
using System.Linq.Expressions;
using Synax.Mapper.Exceptions;
using Synax.Mapper.Internal;

namespace Synax.Mapper.Core;

/// <summary>
/// Immutable container for all compiled <see cref="TypeMap"/> instances.
/// Build once at startup via the constructor, then inject or share freely.
/// <para>
/// <b>Example:</b>
/// <code>
/// var config = new MapperConfiguration(cfg =&gt;
/// {
///     cfg.AddProfile&lt;OrderProfile&gt;();
///     cfg.AddProfile&lt;CustomerProfile&gt;();
/// });
/// IMapper mapper = config.CreateMapper();
/// </code>
/// </para>
/// </summary>
public sealed class MapperConfiguration
{
    private readonly FrozenDictionary<(Type, Type), TypeMap> _typeMaps;

    // Caches base-type resolutions for runtime source types not registered directly
    // (e.g. EF Core lazy-loading proxies deriving from the mapped entity type).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, Type), TypeMap> _runtimeSourceMaps = new();

    public MapperConfiguration(Action<MapperConfigurationExpression> configure)
        : this(Apply(configure))
    {
    }

    /// <summary>
    /// Builds a configuration from an expression that profiles have already been added to.
    /// Use this to compose one configuration from several independent sources — e.g. modules
    /// each contributing their profiles — since the resulting maps are frozen and a second
    /// <see cref="MapperConfiguration"/> cannot be merged into the first.
    /// </summary>
    public MapperConfiguration(MapperConfigurationExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _typeMaps = BuildTypeMaps(expression.Builders, expression.Converters);
    }

    private static MapperConfigurationExpression Apply(Action<MapperConfigurationExpression> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var expression = new MapperConfigurationExpression();
        configure(expression);
        return expression;
    }

    /// <summary>
    /// Creates an <see cref="IMapper"/> backed by this configuration.
    /// </summary>
    public IMapper CreateMapper() => new Internal.Mapper(this);

    /// <summary>
    /// Verifies that every registered mapping produces a value for all destination
    /// members (public writable properties). Call once at startup or from a single
    /// unit test to catch renamed, mistyped, or forgotten properties — without it,
    /// unmapped members are silently left at their default value.
    /// Members excluded with <c>.Ignore(...)</c> are considered intentional and pass.
    /// </summary>
    /// <exception cref="MappingValidationException">
    /// At least one mapping has unmapped destination members. The exception lists
    /// every offending map and member.
    /// </exception>
    public void AssertConfigurationIsValid()
    {
        var invalidMaps = _typeMaps.Values
            .Where(map => map.UnmappedMembers.Count > 0)
            .OrderBy(map => map.SourceType.Name)
            .ThenBy(map => map.DestinationType.Name)
            .ToList();

        if (invalidMaps.Count > 0)
            throw new MappingValidationException(invalidMaps);
    }

    /// <summary>
    /// Retrieves the <see cref="TypeMap"/> for the given source → destination pair.
    /// </summary>
    /// <exception cref="MappingNotFoundException">No mapping was registered.</exception>
    internal TypeMap GetTypeMap(Type sourceType, Type destType)
    {
        if (_typeMaps.TryGetValue((sourceType, destType), out var map))
            return map;

        throw new MappingNotFoundException(sourceType, destType);
    }

    /// <summary>
    /// Retrieves the <see cref="TypeMap"/> for a source type observed at runtime,
    /// which may be a subclass of the registered source type — e.g. an EF Core
    /// lazy-loading proxy or a derived entity. Falls back to walking the source
    /// type's inheritance chain; resolutions are cached per runtime type.
    /// </summary>
    /// <exception cref="MappingNotFoundException">
    /// No mapping was registered for the type or any of its base types.
    /// </exception>
    internal TypeMap GetTypeMapForRuntimeSource(Type sourceType, Type destType)
    {
        if (_typeMaps.TryGetValue((sourceType, destType), out var map))
            return map;

        return _runtimeSourceMaps.GetOrAdd((sourceType, destType), static (key, maps) =>
        {
            var (src, dst) = key;
            for (var baseType = src.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                if (maps.TryGetValue((baseType, dst), out var baseMap))
                    return baseMap;
            }

            throw new MappingNotFoundException(src, dst);
        }, _typeMaps);
    }
    
    // -------- Build pipeline --------

    private static FrozenDictionary<(Type, Type), TypeMap> BuildTypeMaps(
        Dictionary<(Type, Type), IMappingBuilder> builders,
        Dictionary<(Type, Type), LambdaExpression> converters)
    {
        // Lazy cache: when a builder is compiled, its TypeMap is stored here
        // so nested/collection references can resolve it immediately.
        var compiled = new Dictionary<(Type, Type), TypeMap>();

        // Ordered chain of maps currently being compiled. A nested mapping is inlined
        // into its parent expression, so a map that (directly or transitively) references
        // itself would inline forever — detect the cycle and fail with a clear error.
        var inProgress = new List<(Type Source, Type Dest)>();

        LambdaExpression? ResolveConverter(Type src, Type dst)
            => converters.GetValueOrDefault((src, dst));

        TypeMap? Resolve(Type src, Type dst)
        {
            (Type Source, Type Dest) key = (src, dst);
            if (compiled.TryGetValue(key, out var existing))
                return existing;

            // Not compiled yet — try to compile on demand (supports forward references)
            if (!builders.TryGetValue(key, out var builder))
                return null;

            if (inProgress.Contains(key))
            {
                var path = string.Join(" -> ", inProgress
                    .SkipWhile(k => k != key)
                    .Append(key)
                    .Select(k => $"'{k.Source.Name} → {k.Dest.Name}'"));

                throw new InvalidOperationException(
                    $"Cyclic mapping detected: {path}. Nested mappings are inlined into the " +
                    "parent expression, so a mapping cannot reference itself directly or " +
                    "transitively. Ignore the member that closes the cycle (.Ignore(...)) " +
                    "or map it explicitly to a bounded depth with .For(...).");
            }

            inProgress.Add(key);
            try
            {
                var typeMap = CompileBuilder(builder, src, dst, Resolve, ResolveConverter);
                compiled[key] = typeMap;
                return typeMap;
            }
            finally
            {
                inProgress.RemoveAt(inProgress.Count - 1);
            }
        }

        // Compile all registered builders
        foreach (var (src, dst) in builders.Keys)
            Resolve(src, dst);

        return compiled.ToFrozenDictionary();
    }

    private static TypeMap CompileBuilder(
        IMappingBuilder builder,
        Type src,
        Type dst,
        Func<Type, Type, TypeMap?> resolveMap,
        Func<Type, Type, LambdaExpression?> resolveConverter)
    {
        var result = builder.Build(new MappingBuildContext(resolveMap, resolveConverter));

        // Nested maps are already normalized when inlined, so their parameters surface here too.
        var expression = (LambdaExpression)MapParameterBinder.Normalize(result.Expression);
        var parameters = MapParameterBinder.Collect(expression, src, dst);

        return new TypeMap(expression, result.UpdateExpression, src, dst, result.UnmappedMembers, parameters);
    }
}

/// <summary>
/// Passed to the <see cref="MapperConfiguration"/> constructor delegate.
/// Collects <see cref="MapperProfile"/> instances.
/// </summary>
public sealed class MapperConfigurationExpression
{
    internal Dictionary<(Type, Type), IMappingBuilder> Builders { get; } = [];

    internal Dictionary<(Type, Type), LambdaExpression> Converters { get; } = [];

    /// <summary>
    /// Registers all mappings defined in a <see cref="MapperProfile"/>.
    /// </summary>
    public MapperConfigurationExpression AddProfile<TProfile>() where TProfile : MapperProfile, new()
        => AddProfile(new TProfile());

    /// <summary>
    /// Registers all mappings from an existing <see cref="MapperProfile"/> instance.
    /// </summary>
    public MapperConfigurationExpression AddProfile(MapperProfile profile)
    {
        foreach (var (key, builder) in profile.Builders)
        {
            if (!Builders.TryAdd(key, builder))
            {
                throw new InvalidOperationException(
                    $"Duplicate mapping: '{key.Source.Name}' → '{key.Dest.Name}' " +
                    $"is already registered. Each source→destination pair may only be mapped once.");
            }
        }

        foreach (var (key, converter) in profile.Converters)
        {
            if (!Converters.TryAdd(key, converter))
            {
                throw new InvalidOperationException(
                    $"Duplicate converter: '{key.Source.Name}' → '{key.Dest.Name}' " +
                    $"is already registered. Each source→destination pair may only have one converter.");
            }
        }

        return this;
    }
}
