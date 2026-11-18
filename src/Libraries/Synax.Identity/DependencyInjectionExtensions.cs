using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Synax.Identity;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUserContextScope"/>, the ambient identity override used by
    /// <c>ICurrentUser</c> and by the EF ownership providers.
    /// <para>
    /// Idempotent, and already called for you by <c>AddSynaxCurrentUser</c> (Synax.AspNetCore)
    /// and <c>AddSynaxAmbientOwnership</c> (Synax.EntityFrameworkCore) — call it directly only
    /// in a host that wants the scope on its own.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSynaxUserContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IUserContextScope, UserContextScope>();
        return services;
    }
}
