using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Bookings;

public class CancelBookingEndpoint : IVersionedEndpoint
{
    public string Tag => "Bookings";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}/cancel", Handler).RequireAuthorization();

    private static async Task<NoContent> Handler(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        await sender.SendAsync(new CancelBookingRequest { Id = id }, ct);
        return TypedResults.NoContent();
    }
}
