using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Bookings;

public class GetBookingStatsEndpoint : IVersionedEndpoint
{
    public string Tag => "Bookings";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurant/{restaurantId:guid}/stats", Handler).RequireAuthorization();

    private static async Task<Results<Ok<List<BookingStatusCount>>, NoContent, ProblemHttpResult>> Handler(
        Guid restaurantId,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new GetBookingStatsRequest { RestaurantId = restaurantId }, ct);
        return result.ToHttpResult();
    }
}
