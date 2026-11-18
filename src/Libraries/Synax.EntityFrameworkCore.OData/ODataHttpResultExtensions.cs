using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// HTTP-shaped convenience over <see cref="SynaxQueryODataExtensions"/>: applies the request's
/// OData query options and returns <c>200 Ok</c> with the <see cref="ODataPage"/>, or a
/// <c>400</c> Problem Details response for invalid client queries. Standalone — no dependency on
/// Synax.Results or Synax.AspNetCore.
/// </summary>
public static class ODataHttpResultExtensions
{
    /// <summary>
    /// Applies the request's OData query options to the query and materializes the response.
    /// Invalid or disallowed client queries become <c>400</c> Problem Details (title
    /// <see cref="SynaxODataQueryException.ErrorCode"/>); everything else propagates.
    /// </summary>
    public static async Task<Results<Ok<ODataPage>, ProblemHttpResult>> ToODataHttpResultAsync<T>(
        this ISynaxQuery<T> query,
        HttpRequest request,
        SynaxODataOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return TypedResults.Ok(await query.ToODataPageAsync(request, settings, cancellationToken));
        }
        catch (SynaxODataQueryException ex)
        {
            return TypedResults.Problem(
                title: SynaxODataQueryException.ErrorCode,
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
