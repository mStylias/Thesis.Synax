using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Cuisines;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Cuisines;

/// <summary>
/// Merge-patch served by the generic Synax.Crud patch handler: only properties present in the
/// body are written; an explicit <c>null</c> clears a nullable member.
/// </summary>
public class PatchCuisineEndpoint : IVersionedEndpoint
{
    public string Tag => "Cuisines";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<CuisineResponse>, NoContent, ProblemHttpResult>> Handler(
        Guid id,
        PatchCuisineRequest request,
        ISender sender,
        CancellationToken ct)
    {
        // The route id is authoritative — merge it into the body request before dispatching.
        request.Id = id;

        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult();
    }
}
