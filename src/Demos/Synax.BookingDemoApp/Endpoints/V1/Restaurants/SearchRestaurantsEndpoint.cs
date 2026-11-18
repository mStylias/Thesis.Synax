using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Endpoints;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.EntityFrameworkCore.OData;
using Synax.EntityFrameworkCore.OData.Results;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Endpoints.V1.Restaurants;

/// <summary>
/// OData search over restaurants — the mediator + Results flavor: the handler returns the tenant-
/// scoped query deferred, and the endpoint edge applies the client's
/// <c>$filter/$orderby/$top/$skip/$count/$select/$expand</c>, plus everything
/// <c>AddSynaxOData</c> opted this app into.
/// <list type="bullet">
///   <item><c>?$filter=maxCapacity gt 50&amp;$orderby=name&amp;$count=true</c> — the baseline.</item>
///   <item><c>?$search=harbour</c> — free text over name/address/phone (the members declared in
///     <c>AddPresentation</c>; searching a type that declares none is a 400).</item>
///   <item><c>?$compute=maxCapacity div 4 as tables&amp;$filter=tables ge 10&amp;$select=name,tables</c> —
///     a client-defined value, filterable and selectable.</item>
///   <item><c>?$apply=groupby((cuisineId),aggregate(maxCapacity with sum as seats,$count as venues))</c> —
///     aggregation, answered by a <c>GROUP BY</c>; the rows are the grouped members, so
///     <c>$select</c>/<c>$expand</c> alongside it are a 400.</item>
///   <item>every response caps at the configured page size and carries an
///     <c>@odata.nextLink</c> while more rows follow; the client just follows it.</item>
///   <item>the shape is published: <c>GET /odata/$metadata</c> describes it as the
///     <c>restaurants</c> entity set, so a generic OData client can discover this endpoint before
///     querying it. <c>$format=json</c> is accepted; anything else is a 400.</item>
///   <item><c>$expand</c> reaches nothing here on purpose — the search shape is flat, so a client
///     wanting a restaurant's tables asks for the restaurant. Where a query does expose a
///     navigation, the nested options (<c>$filter</c>/<c>$orderby</c>/<c>$top</c>/<c>$skip</c>/
///     <c>$select</c>/<c>$count</c>, and <c>$levels</c> on a self-referencing one) apply inside it
///     and answer to the same app-wide settings as their top-level namesakes.</item>
/// </list>
/// </summary>
public class SearchRestaurantsEndpoint : IVersionedEndpoint
{
    public string Tag => "Restaurants";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/search", Handler).RequireAuthorization();

    private static async Task<Results<Ok<ODataPage>, ProblemHttpResult>> Handler(
        ISender sender,
        HttpRequest request,
        CancellationToken ct)
    {
        var result = await sender.SendAsync(new SearchRestaurantsRequest(), ct);
        return await result.ToODataHttpResultAsync(request, cancellationToken: ct);
    }
}
