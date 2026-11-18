using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.MenuItems;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.MenuItems;

public class AdjustPricesEndpoint : IVersionedEndpoint
{
    public string Tag => "MenuItems";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/restaurant/{restaurantId:guid}/prices", Handler).RequireAuthorization();

    private static async Task<Results<Ok<int>, NoContent, ProblemHttpResult>> Handler(
        Guid restaurantId,
        AdjustPricesRequest request,
        ISender sender,
        CancellationToken ct)
    {
        request.RestaurantId = restaurantId;
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult();
    }
}
