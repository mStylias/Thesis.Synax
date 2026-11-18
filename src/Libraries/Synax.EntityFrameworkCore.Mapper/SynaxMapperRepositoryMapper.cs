using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mapper.Core;
using Synax.Mapper.Core.Extensions;

namespace Synax.EntityFrameworkCore.Mapper;

/// <summary>
/// The Synax.Mapper implementation of <see cref="IRepositoryMapper"/> — a direct delegation to
/// <see cref="IMapper"/> with no reflection or per-call overhead. Public so tests can construct it
/// around a hand-built <c>MapperConfiguration</c>.
/// </summary>
public sealed class SynaxMapperRepositoryMapper(IMapper mapper) : IRepositoryMapper
{
    public TDestination Map<TDestination>(object source)
        where TDestination : class
        => mapper.Map<TDestination>(source)
            ?? throw new InvalidOperationException(
                $"Mapping '{source.GetType().Name}' to '{typeof(TDestination).Name}' produced null.");

    public IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source)
        where TDestination : class
        => source.ProjectTo<TDestination>(mapper);

    public IQueryable<TDestination> ProjectTo<TDestination>(
        IQueryable source, IReadOnlyDictionary<string, object?>? parameters)
        where TDestination : class
        => source.ProjectTo<TDestination>(mapper, parameters);
}
