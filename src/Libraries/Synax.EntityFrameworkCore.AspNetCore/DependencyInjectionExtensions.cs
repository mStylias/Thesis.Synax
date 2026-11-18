using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Synax.AspNetCore;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.AspNetCore;

/// <summary>
/// Bridges the ambient HTTP principal to the EF layer's user/owner providers.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers <c>ICurrentUser&lt;TKey&gt;</c> (via <c>AddSynaxCurrentUser</c>) and implements
    /// <see cref="IAuditUserProvider"/>, <see cref="ISoftDeleteUserProvider"/>, and
    /// <see cref="IOwnerProvider{TKey}"/> from it — audit stamps, soft-delete stamps, and
    /// ownership scoping all follow the authenticated user with one registration.
    /// <para>
    /// Auth-agnostic: the app configures authentication (bearer, cookies, OIDC provider)
    /// exactly as it would without Synax; this bridge only reads the resulting principal.
    /// Outside an HTTP request or when unauthenticated, all providers return <c>null</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="TKey">
    /// The user/owner key type — must match the entities' key types (string, Guid, int, long
    /// supported out of the box).
    /// </typeparam>
    /// <param name="configure">
    /// Optional bridge configuration — set <see cref="IdentityBridgeOptions.OwnerClaim"/> or
    /// <see cref="IdentityBridgeOptions.OwnerSelector"/> when the owner is not the user
    /// (e.g. multi-tenant apps scoping by a <c>tenant_id</c> claim).
    /// </param>
    public static IServiceCollection AddSynaxEfCoreIdentityBridge<TKey>(
        this IServiceCollection services, Action<IdentityBridgeOptions>? configure = null)
        where TKey : notnull
    {
        services.AddSynaxCurrentUser<TKey>();
        services.Configure(configure ?? (_ => { }));

        services.TryAddScoped<IAuditUserProvider, CurrentUserAuditUserProvider<TKey>>();
        services.TryAddScoped<ISoftDeleteUserProvider, CurrentUserSoftDeleteUserProvider<TKey>>();
        services.TryAddScoped<IOwnerProvider<TKey>, CurrentUserOwnerProvider<TKey>>();

        return services;
    }
}
