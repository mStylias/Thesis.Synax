using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Synax.EntityFrameworkCore.Core;

/// <summary>
/// Resolves services from the <b>application</b> service provider a DbContext was configured with
/// (via <c>AddDbContext</c>), so interceptors constructed at options-build time can fall back to
/// DI registrations per save instead of demanding constructed instances.
/// </summary>
internal static class ApplicationServices
{
    internal static TService? GetService<TService>(DbContext context)
        where TService : class
    {
        var applicationServices = context.GetService<IDbContextOptions>()
            .Extensions.OfType<CoreOptionsExtension>()
            .FirstOrDefault()
            ?.ApplicationServiceProvider;

        return applicationServices?.GetService(typeof(TService)) as TService;
    }
}
