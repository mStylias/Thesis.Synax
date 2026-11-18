using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Bookings;

public class ArchiveOldBookingsEndpoint : IVersionedEndpoint
{
    public string Tag => "Bookings";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/archive", Handler).RequireAuthorization();

    private static async Task<Results<Ok<int>, NoContent, ProblemHttpResult>> Handler(
        ArchiveOldBookingsRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult();
    }
}
