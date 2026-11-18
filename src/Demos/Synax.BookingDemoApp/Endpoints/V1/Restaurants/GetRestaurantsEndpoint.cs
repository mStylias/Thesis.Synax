using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Restaurants;

public class GetRestaurantsEndpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/", Handler).RequireAuthorization();

    private static async Task<Results<Ok<PagedResult<RestaurantResponse>>, NoContent, ProblemHttpResult>> Handler(
        ISender sender,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20)
    {
        var result = await sender.SendAsync(new GetRestaurantsRequest { Page = page, PageSize = pageSize }, ct);
        return result.ToHttpResult();
    }
}
