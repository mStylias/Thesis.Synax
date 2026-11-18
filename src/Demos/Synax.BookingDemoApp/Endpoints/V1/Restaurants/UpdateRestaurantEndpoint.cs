using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Restaurants;

public class UpdateRestaurantEndpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/{id:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<RestaurantResponse>, NoContent, ProblemHttpResult>> Handler(
        Guid id,
        UpdateRestaurantRequest request,
        ISender sender,
        CancellationToken ct)
    {
        // The route id is authoritative — merge it into the body request before dispatching.
        request.Id = id;

        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult();
    }
}
