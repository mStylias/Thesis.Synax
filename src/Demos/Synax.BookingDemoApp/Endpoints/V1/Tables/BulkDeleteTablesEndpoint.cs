using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Tables;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Tables;

public class BulkDeleteTablesEndpoint : IVersionedEndpoint
{
    public string Tag => "Tables";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/restaurant/{restaurantId:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<int>, NoContent, ProblemHttpResult>> Handler(
        Guid restaurantId,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new BulkDeleteTablesRequest { RestaurantId = restaurantId }, ct);
        return result.ToHttpResult();
    }
}
