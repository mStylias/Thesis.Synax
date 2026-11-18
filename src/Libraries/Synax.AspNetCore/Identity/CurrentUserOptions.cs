using System.Security.Claims;

namespace Synax.AspNetCore.Identity;

/// <summary>
/// Configuration for <see cref="ICurrentUser"/>. The only per-app variance is the
/// <em>shape</em> of the principal — which claim carries the user id — never the
/// authentication scheme or transport, which are the app's own concern.
/// </summary>
public sealed class CurrentUserOptions
{
    /// <summary>
    /// Selects the user id claim from the authenticated principal.
    /// Defaults to <see cref="ClaimTypes.NameIdentifier"/> falling back to <c>sub</c> —
    /// forgiving of ASP.NET's inbound-claim remapping (which turns <c>sub</c> into
    /// <c>NameIdentifier</c> unless the app disables <c>MapInboundClaims</c>).
    /// </summary>
    public Func<ClaimsPrincipal, string?> UserIdSelector { get; set; } = DefaultUserIdSelector;

    private static string? DefaultUserIdSelector(ClaimsPrincipal principal)
        => principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? principal.FindFirst("sub")?.Value;
}
