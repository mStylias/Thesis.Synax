using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Synax.Identity;

namespace Synax.AspNetCore.Identity;

/// <summary>
/// Default <see cref="ICurrentUser"/> reading the ambient <see cref="HttpContext.User"/>.
/// A user-replacing <see cref="IUserContextScope"/> override fully replaces the principal
/// (including <c>BecomeSystem</c>, which reports "no user" even mid-request).
/// Outside an HTTP request with no override there is no principal and every member
/// reports "no user" — a first-class state, not an error.
/// </summary>
internal class HttpContextCurrentUser(
    IHttpContextAccessor httpContextAccessor,
    IOptions<CurrentUserOptions> options,
    IUserContextScope userContext) : ICurrentUser
{
    // BecomeAllOwners changes owner scoping only; it leaves the ambient principal to supply the
    // user, so it is not an override as far as ICurrentUser is concerned.
    private protected UserContextOverride? Override
        => userContext.Current is { OverridesUser: true } active ? active : null;

    public ClaimsPrincipal? Principal
        => Override is null ? httpContextAccessor.HttpContext?.User : null;

    public bool IsAuthenticated => Override is { } o
        ? o.UserId is not null
        : Principal?.Identity?.IsAuthenticated == true;

    public string? Id => Override is { } o
        ? o.UserId?.ToString()
        : Principal?.Identity?.IsAuthenticated == true ? options.Value.UserIdSelector(Principal) : null;
}

/// <summary>
/// Default <see cref="ICurrentUser{TKey}"/>: the raw id claim parsed via <see cref="ClaimKeyParser"/>,
/// or the typed override key when a <see cref="IUserContextScope"/> scope is active.
/// </summary>
internal sealed class HttpContextCurrentUser<TKey>(
    IHttpContextAccessor httpContextAccessor,
    IOptions<CurrentUserOptions> options,
    IUserContextScope userContext)
    : HttpContextCurrentUser(httpContextAccessor, options, userContext), ICurrentUser<TKey>
    where TKey : notnull
{
    public bool TryGetId([MaybeNullWhen(false)] out TKey id)
    {
        if (Override is { } o)
        {
            switch (o.UserId)
            {
                case null:
                    id = default;
                    return false;
                case TKey typed:
                    id = typed;
                    return true;
                default:
                    throw new InvalidOperationException(
                        $"The active user context override carries a user id of type " +
                        $"'{o.UserId.GetType().Name}', but '{typeof(TKey).Name}' was requested. " +
                        "Use the same key type in BecomeUser/BecomeOwner as in ICurrentUser<TKey>.");
            }
        }

        var raw = Id;
        if (raw is null)
        {
            id = default;
            return false;
        }

        id = ClaimKeyParser.Parse<TKey>(raw, "user id claim");
        return true;
    }
}
