using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Application.Contracts.Bookings;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Bookings;

public class GetBookingsEndpoint : IVersionedEndpoint
{
    public string Tag => "Bookings";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurant/{restaurantId:guid}", Handler).RequireAuthorization();

    // RestaurantId binds from the route, PageSize/After from the query string — pass the
    // returned NextCursor back as `after` to fetch the next page.
    private static async Task<Results<Ok<KeysetPage<BookingResponse, Guid>>, NoContent, ProblemHttpResult>> Handler(
        Guid restaurantId,
        ISender sender,
        CancellationToken ct,
        int pageSize = 20,
        Guid? after = null)
    {
        var result = await sender.SendAsync(new GetBookingsRequest
        {
            RestaurantId = restaurantId,
            PageSize = pageSize,
            After = after
        }, ct);
        return result.ToHttpResult();
    }
}
