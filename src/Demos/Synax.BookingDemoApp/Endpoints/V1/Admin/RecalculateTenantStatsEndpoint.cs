using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Results;
using Synax.BookingDemoApp.Application.Features.Admin;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Admin;

public class RecalculateTenantStatsEndpoint : IVersionedEndpoint
{
    public string Tag => "Admin";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/tenants/{tenantId:guid}/stats", Handler).RequireAuthorization();

    private static async Task<Results<Ok<TenantStatsResponse>, NoContent, ProblemHttpResult>> Handler(
        Guid tenantId,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new RecalculateTenantStatsRequest { TenantId = tenantId }, ct);
        return result.ToHttpResult();
    }
}
