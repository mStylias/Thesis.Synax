using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Restaurants;

public class CreateRestaurantEndpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/", Handler).RequireAuthorization();

    private static async Task<Results<Created<RestaurantResponse>, NoContent, ProblemHttpResult>> Handler(
        CreateRestaurantRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult(r => TypedResults.Created($"/api/v1/restaurants/{r.Id}", r));
    }
}
