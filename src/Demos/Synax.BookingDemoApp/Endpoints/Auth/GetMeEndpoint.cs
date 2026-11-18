using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Auth;
using Synax.BookingDemoApp.Application.Mapping;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.Auth;

/// <summary>
/// Non-versioned like <see cref="LoginEndpoint"/> — maps to <c>GET /auth/me</c>.
/// </summary>
public class GetMeEndpoint : IEndpoint
{
    public string Tag => "Auth";

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/me", Handler).RequireAuthorization();

    private static async Task<Results<Ok<UserResponse>, NoContent, ProblemHttpResult>> Handler(
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new GetMeRequest(), ct);
        return result.ToHttpResult();
    }
}
