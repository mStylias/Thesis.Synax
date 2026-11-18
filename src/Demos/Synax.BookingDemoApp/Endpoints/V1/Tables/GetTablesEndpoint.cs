using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Tables;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Tables;

public class GetTablesEndpoint : IVersionedEndpoint
{
    public string Tag => "Tables";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurant/{restaurantId:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<List<TableResponse>>, NoContent, ProblemHttpResult>> Handler(
        Guid restaurantId,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new GetTablesRequest { RestaurantId = restaurantId }, ct);
        return result.ToHttpResult();
    }
}
