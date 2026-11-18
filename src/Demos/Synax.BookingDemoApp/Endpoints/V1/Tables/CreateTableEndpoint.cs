using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Tables;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Tables;

public class CreateTableEndpoint : IVersionedEndpoint
{
    public string Tag => "Tables";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/", Handler).RequireAuthorization();

    private static async Task<Results<Created<TableResponse>, NoContent, ProblemHttpResult>> Handler(
        CreateTableRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult(table => TypedResults.Created($"/api/v1/tables/{table.Id}", table));
    }
}
