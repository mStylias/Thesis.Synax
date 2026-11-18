using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.Identity;

namespace Synax.EntityFrameworkCore.Ownership;

/// <summary>
/// An <see cref="IOwnerProvider{TKey}"/> backed purely by <see cref="IUserContextScope"/> — no
/// principal, no HTTP. This is the ownership provider for hosts that have no ambient user to
/// derive an owner from: workers, seeders, console tools, tests. A web host uses
/// <c>AddSynaxEfCoreIdentityBridge</c> instead, which reads the principal and consults the same
/// scope on top.
/// </summary>
internal sealed class AmbientOwnerProvider<TKey>(IUserContextScope userContext) : IOwnerProvider<TKey>
    where TKey : notnull
{
    public object? GetCurrentOwnerId()
        => userContext.Current switch
        {
            null or { AllOwners: true } => null,
            { OwnerId: null } => null,
            { OwnerId: TKey typed } => typed,
            var active => throw new InvalidOperationException(
                $"The active user context override carries an owner id of type " +
                $"'{active.OwnerId!.GetType().Name}', but the ownership key type is '{typeof(TKey).Name}'. " +
                "Use the same key type in BecomeOwner/BecomeUser as in AddSynaxAmbientOwnership<TKey>.")
        };

    public bool IsCrossOwner() => userContext.Current is { AllOwners: true };
}

public static class AmbientOwnershipServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUserContextScope"/> and an <see cref="IOwnerProvider{TKey}"/> that
    /// follows it — the ownership registration for a host with no HTTP principal. Ownership
    /// scoping is then driven entirely by <c>BecomeOwner</c> / <c>BecomeAllOwners</c> /
    /// <c>BecomeSystem</c>.
    /// <para>
    /// Uses <c>TryAdd</c>, so calling <c>AddSynaxEfCoreIdentityBridge&lt;TKey&gt;</c> first (a web
    /// host) keeps the principal-aware provider; the two are safe to have in one composition root.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSynaxAmbientOwnership<TKey>(this IServiceCollection services)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSynaxUserContext();
        services.TryAddScoped<IOwnerProvider<TKey>, AmbientOwnerProvider<TKey>>();

        return services;
    }
}
