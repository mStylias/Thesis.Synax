using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Reviews;
using Synax.BookingDemoApp.Application.Contracts.Reviews;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Reviews;

public class CreateReviewEndpoint : IVersionedEndpoint
{
    public string Tag => "Reviews";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/", Handler).RequireAuthorization();

    private static async Task<Results<Created<ReviewResponse>, NoContent, ProblemHttpResult>> Handler(
        CreateReviewRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(request, ct);
        return result.ToHttpResult(review => TypedResults.Created($"/api/v1/reviews/{review.Id}", review));
    }
}
