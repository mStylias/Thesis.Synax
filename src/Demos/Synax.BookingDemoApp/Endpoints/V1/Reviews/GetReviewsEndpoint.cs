using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Reviews;
using Synax.BookingDemoApp.Application.Contracts.Reviews;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Reviews;

public class GetReviewsEndpoint : IVersionedEndpoint
{
    public string Tag => "Reviews";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurant/{restaurantId:guid}", Handler).RequireAuthorization();

    private static async Task<Results<Ok<KeysetPage<ReviewResponse, Guid>>, NoContent, ProblemHttpResult>> Handler(
        Guid restaurantId,
        ISender sender,
        CancellationToken ct,
        int pageSize = 20,
        Guid? after = null)
    {
        var result = await sender.SendAsync(new GetReviewsRequest
        {
            RestaurantId = restaurantId,
            PageSize = pageSize,
            After = after
        }, ct);
        return result.ToHttpResult();
    }
}
