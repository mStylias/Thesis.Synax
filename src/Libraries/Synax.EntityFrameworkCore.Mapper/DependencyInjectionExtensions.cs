using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mapper.Core;

namespace Synax.EntityFrameworkCore.Mapper;

/// <summary>
/// Registers Synax.Mapper as the repositories' <see cref="IRepositoryMapper"/>.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Bridges the repositories' DTO features (<c>QueryDto</c>, <c>AddDtoAsync</c>) to Synax.Mapper.
    /// Requires an <see cref="IMapper"/> registration (<c>AddSynaxMapper</c>); the mapper is resolved
    /// lazily, so registration order does not matter. Uses <c>TryAdd</c> — an already registered
    /// <see cref="IRepositoryMapper"/> wins.
    /// </summary>
    public static IServiceCollection AddSynaxRepositoryMapper(this IServiceCollection services)
    {
        services.TryAddSingleton<IRepositoryMapper>(
            provider => new SynaxMapperRepositoryMapper(provider.GetRequiredService<IMapper>()));

        return services;
    }
}
