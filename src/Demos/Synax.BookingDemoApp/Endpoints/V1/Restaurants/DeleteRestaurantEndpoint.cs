using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Restaurants;

public class DeleteRestaurantEndpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", Handler).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> Handler(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new DeleteRestaurantRequest { Id = id }, ct);
        return result.ToHttpResult();
    }
}
