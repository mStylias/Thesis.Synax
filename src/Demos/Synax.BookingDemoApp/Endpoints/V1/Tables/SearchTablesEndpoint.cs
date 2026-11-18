using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.OData;

namespace Synax.BookingDemoApp.Endpoints.V1.Tables;

/// <summary>
/// OData search over tables — the standalone flavor: no mediator, no Results, just the repository
/// query and the OData package. Ownership and soft-delete scoping still apply because the query
/// goes through the same repository, and the app-wide <c>AddSynaxOData</c> settings apply here too:
/// <list type="bullet">
///   <item><c>?$filter=seats ge 4 and location eq 'Terrace'&amp;$orderby=number</c> — the baseline.</item>
///   <item><c>?$compute=seats mul 2 as standingRoom&amp;$orderby=standingRoom desc</c> — <c>$compute</c>
///     is app-wide, so it works here without this endpoint knowing about it.</item>
///   <item><c>?$apply=groupby((location),aggregate(seats with sum as capacity))</c> — <c>$apply</c> is
///     app-wide too. Grouping over the entity's enum works here; grouping over the DTO's
///     <c>location</c> hits the documented enum-projection limit, same as filtering does.</item>
///   <item>server-driven paging likewise: <c>@odata.nextLink</c> appears once there are more tables
///     than the configured page size — carrying a <c>$skip</c> offset rather than a cursor when a
///     <c>$apply</c> reshaped the rows.</item>
///   <item>published as the <c>tables</c> entity set in <c>GET /odata/$metadata</c>, alongside
///     <c>restaurants</c> — one service document for both endpoints.</item>
///   <item><c>?$search=…</c> is a deliberate 400 — <c>TableResponse</c> declares no searchable
///     members (its only string, <c>Location</c>, is an enum projection) — and stays one when a
///     client nests it inside someone else's <c>$expand=tables(...)</c>.</item>
/// </list>
/// </summary>
public class SearchTablesEndpoint : IVersionedEndpoint
{
    public string Tag => "Tables";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/search", Handler).RequireAuthorization();

    private static Task<Results<Ok<ODataPage>, ProblemHttpResult>> Handler(
        IUnitOfWork<BookingContext> unitOfWork,
        HttpRequest request,
        CancellationToken ct)
        => unitOfWork
            .GetRepository<Table>()
            .QueryDto<TableResponse>()
            .ToODataHttpResultAsync(request, cancellationToken: ct);
}
