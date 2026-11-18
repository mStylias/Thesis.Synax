using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Auth;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.Auth;

/// <summary>
/// Deliberately non-versioned: auth sits outside the versioned API surface, so this implements
/// plain <see cref="IEndpoint"/> and maps under <c>/auth/</c> (the <c>/{tag}/</c> grouping)
/// instead of <c>/api/v{version}/...</c>. Everything else in the app is versioned.
/// </summary>
public class LoginEndpoint : IEndpoint
{
    public string Tag => "Auth";

    // Anonymous on purpose — this is how a client obtains a token in the first place.
    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/login", Handler);

    private static async Task<Results<Ok<LoginResponse>, NoContent, ProblemHttpResult>> Handler(
        LoginRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult();
    }
}
