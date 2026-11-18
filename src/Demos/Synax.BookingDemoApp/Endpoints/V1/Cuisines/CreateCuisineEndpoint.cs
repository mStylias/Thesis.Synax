using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Cuisines;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Cuisines;

public class CreateCuisineEndpoint : IVersionedEndpoint
{
    public string Tag => "Cuisines";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/", Handler).RequireAuthorization();

    private static async Task<Results<Created<CuisineResponse>, NoContent, ProblemHttpResult>> Handler(
        CreateCuisineRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult(cuisine => TypedResults.Created($"/api/v1/cuisines/{cuisine.Id}", cuisine));
    }
}
