using System.Security.Claims;

namespace Synax.EntityFrameworkCore.AspNetCore;

/// <summary>
/// Configuration for the identity bridge. Ownership is deliberately independent of identity:
/// in multi-tenant apps the owner is the tenant/organization while audit stamps are the
/// individual user. When neither <see cref="OwnerClaim"/> nor <see cref="OwnerSelector"/>
/// is set, the owner defaults to the current user's id.
/// </summary>
public sealed class IdentityBridgeOptions
{
    /// <summary>
    /// The claim carrying the owner id (e.g. <c>"tenant_id"</c>).
    /// Ignored when <see cref="OwnerSelector"/> is set.
    /// </summary>
    public string? OwnerClaim { get; set; }

    /// <summary>
    /// Full-control owner selection from the principal, for shapes a single claim can't
    /// express. Takes precedence over <see cref="OwnerClaim"/>.
    /// </summary>
    public Func<ClaimsPrincipal, string?>? OwnerSelector { get; set; }
}
