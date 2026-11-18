using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Application.Contracts.Bookings;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Bookings;

public class CreateBookingEndpoint : IVersionedEndpoint
{
    public string Tag => "Bookings";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/", Handler).RequireAuthorization();

    private static async Task<Results<Created<BookingResponse>, NoContent, ProblemHttpResult>> Handler(
        CreateBookingRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult(booking => TypedResults.Created($"/api/v1/bookings/{booking.Id}", booking));
    }
}
