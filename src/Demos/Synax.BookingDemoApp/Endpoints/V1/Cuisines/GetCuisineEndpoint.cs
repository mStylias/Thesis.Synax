using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Cuisines;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Cuisines;

/// <summary>Served by the generic Synax.Crud get handler — no handwritten handler exists.</summary>
public class GetCuisineEndpoint : IVersionedEndpoint
{
    public string Tag => "Cuisines";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/{id:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<CuisineResponse>, NoContent, ProblemHttpResult>> Handler(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new GetCuisineRequest { Id = id }, ct);
        return result.ToHttpResult();
    }
}
