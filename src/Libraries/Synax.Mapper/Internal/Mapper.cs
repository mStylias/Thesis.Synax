using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Synax.Mapper.Core;

namespace Synax.Mapper.Internal;

/// <summary>
/// Handles the mapping of objects from source types to destination types based on the configuration profiles.
/// </summary>
internal sealed class Mapper : IMapper
{
    private readonly MapperConfiguration _configuration;

    internal Mapper(MapperConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Map<TDest>(object? source)
    {
        if (source is null)
            return default;

        var typeMap = _configuration.GetTypeMapForRuntimeSource(source.GetType(), typeof(TDest));
        return (TDest)typeMap.UntypedDelegate(source);
    }

    /// <inheritdoc />
    public TDest Map<TDest>(object? source, TDest destination) where TDest : class
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (source is null)
            return destination;

        var typeMap = _configuration.GetTypeMapForRuntimeSource(source.GetType(), typeof(TDest));
        typeMap.UntypedUpdateDelegate(source, destination);
        return destination;
    }

    /// <inheritdoc />
    public List<TDest> MapList<TDest>(IEnumerable<object?>? source)
    {
        if (source is null)
            return [];

        var list = source is System.Collections.ICollection c
            ? new List<TDest>(c.Count)
            : new List<TDest>();

        // Resolve the delegate per element type so heterogeneous lists dispatch
        // correctly; the cached pair makes homogeneous lists (the common case)
        // resolve only once.
        Type? currentType = null;
        Func<object, object>? func = null;

        foreach (var item in source)
        {
            if (item is null)
            {
                list.Add(default!);
                continue;
            }

            var itemType = item.GetType();
            if (itemType != currentType)
            {
                func = _configuration.GetTypeMapForRuntimeSource(itemType, typeof(TDest)).UntypedDelegate;
                currentType = itemType;
            }

            list.Add((TDest)func!(item));
        }

        return list;
    }

    /// <inheritdoc />
    public Expression<Func<TSource, TDest>> GetExpression<TSource, TDest>()
        => GetExpression<TSource, TDest>(parameters: null);

    /// <inheritdoc />
    public Expression<Func<TSource, TDest>> GetExpression<TSource, TDest>(IReadOnlyDictionary<string, object?>? parameters)
    {
        var typeMap = _configuration.GetTypeMap(typeof(TSource), typeof(TDest));
        return (Expression<Func<TSource, TDest>>)typeMap.GetExpression(parameters);
    }

    /// <inheritdoc />
    public TypeMap GetTypeMap<TSource, TDest>()
        => _configuration.GetTypeMap(typeof(TSource), typeof(TDest));

    /// <inheritdoc />
    public TypeMap GetTypeMap(Type sourceType, Type destinationType)
        => _configuration.GetTypeMap(sourceType, destinationType);
}
