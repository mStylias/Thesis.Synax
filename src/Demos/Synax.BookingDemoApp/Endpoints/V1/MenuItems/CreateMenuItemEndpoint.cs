using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.MenuItems;
using Synax.BookingDemoApp.Application.Contracts.MenuItems;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.MenuItems;

public class CreateMenuItemEndpoint : IVersionedEndpoint
{
    public string Tag => "MenuItems";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/", Handler).RequireAuthorization();

    private static async Task<Results<Created<MenuItemResponse>, NoContent, ProblemHttpResult>> Handler(
        CreateMenuItemRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult(item =>
            TypedResults.Created($"/api/v1/menuitems/restaurant/{item.RestaurantId}", item));
    }
}
