using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Errors;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.OData;
using Synax.Results;

namespace Synax.EntityFrameworkCore.OData.Results;

/// <summary>
/// Bridges the OData query helpers to Synax.Results: mediator handlers return a deferred
/// <c>Result&lt;ISynaxQuery&lt;TDto&gt;, HttpError&gt;</c> and the endpoint applies the request's
/// OData query options at the edge.
/// </summary>
public static class ODataResultExtensions
{
    /// <summary>
    /// Converts a deferred-query <see cref="Result{TData, TError}"/> to an HTTP response by
    /// applying the request's OData query options and materializing one <see cref="ODataPage"/>.
    /// <list type="bullet">
    ///   <item>Success returns <c>200 OK</c> with the page.</item>
    ///   <item>A failed result returns its <see cref="HttpError"/> as Problem Details.</item>
    ///   <item>An invalid client query returns <c>400</c> Problem Details
    ///     (error code <see cref="SynaxODataQueryException.ErrorCode"/>).</item>
    /// </list>
    /// </summary>
    public static async Task<Results<Ok<ODataPage>, ProblemHttpResult>> ToODataHttpResultAsync<TDto>(
        this Result<ISynaxQuery<TDto>, HttpError> result,
        HttpRequest request,
        SynaxODataOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        if (!result.IsSuccess)
            return result.Error!.ToProblemResult();

        try
        {
            return TypedResults.Ok(await result.Data!.ToODataPageAsync(request, settings, cancellationToken));
        }
        catch (SynaxODataQueryException ex)
        {
            return HttpError
                .BadRequest(ex.Message, errorCode: SynaxODataQueryException.ErrorCode)
                .ToProblemResult();
        }
    }
}
