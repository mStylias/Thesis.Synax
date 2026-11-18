using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.MenuItems;
using Synax.BookingDemoApp.Application.Contracts.MenuItems;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.MenuItems;

public class GetMenuEndpoint : IVersionedEndpoint
{
    public string Tag => "MenuItems";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurant/{restaurantId:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<List<MenuItemResponse>>, NoContent, ProblemHttpResult>> Handler(
        Guid restaurantId,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new GetMenuRequest { RestaurantId = restaurantId }, ct);
        return result.ToHttpResult();
    }
}
