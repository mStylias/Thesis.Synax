namespace Synax.EntityFrameworkCore.Abstractions.Core;

/// <summary>
/// The mapping bridge used by repositories for their DTO features (<c>QueryDto</c>,
/// <c>AddDtoAsync</c>, <c>AddDtoAndSaveChangesAsync</c>). Repositories carry no dependency on any
/// specific mapping library — implement this interface to plug one in.
/// <para>
/// A registration is only required when the DTO features are actually used. The companion package
/// <c>Synax.EntityFrameworkCore.Mapper</c> provides the Synax.Mapper implementation via
/// <c>services.AddSynaxRepositoryMapper()</c>; adapters for other mappers (AutoMapper, Mapster, ...)
/// are a few lines each.
/// </para>
/// </summary>
public interface IRepositoryMapper
{
    /// <summary>
    /// Maps <paramref name="source"/> to a new <typeparamref name="TDestination"/> instance.
    /// Implementations must not return <c>null</c> for a non-null source — throw instead.
    /// </summary>
    TDestination Map<TDestination>(object source) where TDestination : class;

    /// <summary>
    /// Projects the queryable to <typeparamref name="TDestination"/> by injecting a
    /// provider-translatable <c>Select</c> into the expression tree (no client-side evaluation).
    /// </summary>
    IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source) where TDestination : class;

    /// <summary>
    /// Projects as <see cref="ProjectTo{TDestination}(IQueryable)"/>, supplying the per-call values the
    /// mapping declares (e.g. the request's language). Implementations must fail when a value the
    /// mapping reads is missing, never bind <c>null</c>. A mapper without parameters supports only an
    /// empty set.
    /// </summary>
    IQueryable<TDestination> ProjectTo<TDestination>(
        IQueryable source, IReadOnlyDictionary<string, object?>? parameters) where TDestination : class
        => parameters is null or { Count: 0 }
            ? ProjectTo<TDestination>(source)
            : throw new NotSupportedException(
                $"'{GetType().Name}' does not support mapping parameters; '{typeof(TDestination).Name}' was projected with " +
                string.Join(", ", parameters.Keys) + ".");
}
