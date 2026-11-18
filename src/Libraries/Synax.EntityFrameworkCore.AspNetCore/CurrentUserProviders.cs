using Microsoft.Extensions.Options;
using Synax.AspNetCore.Identity;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.Identity;

namespace Synax.EntityFrameworkCore.AspNetCore;

/// <summary>
/// Implements <see cref="IAuditUserProvider"/> from the current authenticated user.
/// Returns <c>null</c> when no user is ambient (unauthenticated calls, background work).
/// </summary>
internal sealed class CurrentUserAuditUserProvider<TKey>(ICurrentUser<TKey> currentUser)
    : IAuditUserProvider where TKey : notnull
{
    public object? GetCurrentUserId() => currentUser.TryGetId(out var id) ? id : null;
}

/// <summary>
/// Implements <see cref="ISoftDeleteUserProvider"/> from the current authenticated user.
/// Returns <c>null</c> when no user is ambient (unauthenticated calls, background work).
/// </summary>
internal sealed class CurrentUserSoftDeleteUserProvider<TKey>(ICurrentUser<TKey> currentUser)
    : ISoftDeleteUserProvider where TKey : notnull
{
    public object? GetCurrentUserId() => currentUser.TryGetId(out var id) ? id : null;
}

/// <summary>
/// Implements <see cref="IOwnerProvider{TKey}"/> from the current principal: the configured
/// owner selector/claim when set, otherwise the current user's id. An active
/// <see cref="IUserContextScope"/> override takes precedence (its owner key, claim config
/// does not apply). Returns <c>null</c> when no owner is ambient, so ownership-scoped
/// queries see only shared rows.
/// </summary>
internal sealed class CurrentUserOwnerProvider<TKey>(
    ICurrentUser<TKey> currentUser, IUserContextScope userContext, IOptions<IdentityBridgeOptions> options)
    : IOwnerProvider<TKey> where TKey : notnull
{
    public bool IsCrossOwner() => userContext.Current is { AllOwners: true };

    public object? GetCurrentOwnerId()
    {
        // Owner scoping is lifted, so no single owner is current — reads widen via the query
        // filter's cross-owner term, writes have nothing to stamp and say so.
        if (userContext.Current is { AllOwners: true })
            return null;

        if (userContext.Current is { } o)
        {
            return o.OwnerId switch
            {
                null => null,
                TKey typed => typed,
                _ => throw new InvalidOperationException(
                    $"The active user context override carries an owner id of type " +
                    $"'{o.OwnerId.GetType().Name}', but the ownership key type is '{typeof(TKey).Name}'. " +
                    "Use the same key type in BecomeOwner/BecomeUser as in AddSynaxEfCoreIdentityBridge<TKey>.")
            };
        }

        var opts = options.Value;

        if (opts.OwnerSelector is null && opts.OwnerClaim is null)
            return currentUser.TryGetId(out var id) ? id : null;

        if (!currentUser.IsAuthenticated)
            return null;

        var principal = currentUser.Principal!;
        var raw = opts.OwnerSelector is not null
            ? opts.OwnerSelector(principal)
            : principal.FindFirst(opts.OwnerClaim!)?.Value;

        return raw is null ? null : ClaimKeyParser.Parse<TKey>(raw, "owner claim");
    }
}
