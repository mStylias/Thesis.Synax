using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Synax.AspNetCore.Identity;

/// <summary>
/// The current authenticated user, derived from the ambient <see cref="ClaimsPrincipal"/>.
/// Auth-agnostic by construction: every authentication scheme (OIDC/JWT bearer, cookies, ...)
/// materializes a principal before the request reaches application code, and this abstraction
/// sits strictly above it — Synax never configures authentication itself.
/// Register via <c>services.AddSynaxCurrentUser()</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Whether the ambient principal is authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The raw user id claim value selected by <see cref="CurrentUserOptions.UserIdSelector"/>,
    /// or <c>null</c> when unauthenticated or outside an HTTP request (e.g. background work).
    /// </summary>
    string? Id { get; }

    /// <summary>
    /// The ambient principal — the escape hatch for claims this abstraction deliberately
    /// does not model (roles, tenant, email, ...). <c>null</c> outside an HTTP request.
    /// </summary>
    ClaimsPrincipal? Principal { get; }
}

/// <summary>
/// <see cref="ICurrentUser"/> with the user id parsed to a typed key
/// (<c>Guid</c>, <c>int</c>, <c>long</c>, and <c>string</c> are supported out of the box).
/// Register via <c>services.AddSynaxCurrentUser&lt;TKey&gt;()</c>.
/// </summary>
public interface ICurrentUser<TKey> : ICurrentUser where TKey : notnull
{
    /// <summary>
    /// Retrieves the current user's typed id. Returns <c>false</c> when unauthenticated or
    /// outside an HTTP request — a deliberate shape: an unconstrained <c>TKey?</c> property
    /// would silently yield <c>default</c> (e.g. <c>Guid.Empty</c>) for value-type keys.
    /// </summary>
    bool TryGetId([MaybeNullWhen(false)] out TKey id);
}
