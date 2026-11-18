using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.RestaurantsV2;
using Synax.BookingDemoApp.Application.Contracts.RestaurantsV2;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V2.Restaurants;

/// <summary>
/// <c>Version =&gt; new(2)</c> routes this under <c>/api/v2/restaurants/</c> and gives it its
/// own OpenAPI group (<c>v2</c>) — same "Restaurants" tag as the v1 endpoints, demonstrating
/// multi-version grouping of one resource.
/// </summary>
public class GetRestaurantsV2Endpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(2);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/", Handler).RequireAuthorization();

    private static async Task<Results<Ok<KeysetPage<RestaurantV2Response, Guid>>, NoContent, ProblemHttpResult>> Handler(
        ISender sender,
        CancellationToken ct,
        int pageSize = 20,
        Guid? after = null)
    {
        var result = await sender.SendAsync(
            new GetRestaurantsV2Request { PageSize = pageSize, After = after }, ct);
        return result.ToHttpResult();
    }
}
