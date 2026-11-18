using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Tables;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Tables;

public class HardDeleteTableEndpoint : IVersionedEndpoint
{
    public string Tag => "Tables";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}/permanent", Handler).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> Handler(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new HardDeleteTableRequest { Id = id }, ct);
        return result.ToHttpResult();
    }
}
