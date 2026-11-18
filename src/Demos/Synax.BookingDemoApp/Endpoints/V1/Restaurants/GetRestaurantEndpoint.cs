using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Restaurants;

public class GetRestaurantEndpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/{id:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<RestaurantDetailResponse>, NoContent, ProblemHttpResult>> Handler(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new GetRestaurantRequest { Id = id }, ct);
        return result.ToHttpResult();
    }
}
