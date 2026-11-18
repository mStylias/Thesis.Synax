using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Cuisines;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Cuisines;

public class GetCuisinesEndpoint : IVersionedEndpoint
{
    public string Tag => "Cuisines";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/", Handler).RequireAuthorization();

    private static async Task<Results<Ok<List<CuisineResponse>>, NoContent, ProblemHttpResult>> Handler(
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new GetCuisinesRequest(), ct);
        return result.ToHttpResult();
    }
}
