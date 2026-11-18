using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Cuisines;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Cuisines;

/// <summary>Served by the generic Synax.Crud delete handler (hard delete — Cuisine is not soft-deletable).</summary>
public class DeleteCuisineEndpoint : IVersionedEndpoint
{
    public string Tag => "Cuisines";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", Handler).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> Handler(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new DeleteCuisineRequest { Id = id }, ct);
        return result.ToHttpResult();
    }
}
