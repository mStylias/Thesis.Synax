using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Restaurants;

public class RestoreRestaurantEndpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/restore", Handler).RequireAuthorization();

    private static async Task<Results<Ok<RestaurantResponse>, NoContent, ProblemHttpResult>> Handler(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new RestoreRestaurantRequest { Id = id }, ct);
        return result.ToHttpResult();
    }
}
