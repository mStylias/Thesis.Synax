using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Errors;
using Synax.Results;

namespace Synax.AspNetCore.Results;

/// <summary>
/// Extension methods for converting <see cref="Result{TData, TError}"/> to typed HTTP responses.
/// The returned unions surface accurate response schemas in OpenAPI/Scalar.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts a <see cref="Result{TData, HttpError}"/> to an HTTP response.
    /// <list type="bullet">
    ///   <item>Success with data returns <c>200 OK</c> with the data body.</item>
    ///   <item>Success with <c>null</c> data returns <c>204 No Content</c>.</item>
    ///   <item>Error returns an RFC 9457 Problem Details response derived from the <see cref="HttpError"/>.</item>
    /// </list>
    /// </summary>
    public static Results<Ok<TData>, NoContent, ProblemHttpResult> ToHttpResult<TData>(
        this Result<TData, HttpError> result)
    {
        if (result.IsSuccess)
            return result.Data is not null
                ? TypedResults.Ok(result.Data)
                : TypedResults.NoContent();

        return result.Error!.ToProblemResult();
    }

    /// <summary>
    /// Converts a <see cref="Result{TData, HttpError}"/> to an HTTP response with a custom success shape,
    /// e.g. <c>201 Created</c> for POST endpoints:
    /// <c>result.ToHttpResult(data => TypedResults.Created($"/bookings/{data.Id}", data))</c>.
    /// Success with <c>null</c> data returns <c>204 No Content</c> without invoking the factory.
    /// The error path is unchanged: an RFC 9457 Problem Details response derived from the <see cref="HttpError"/>.
    /// </summary>
    public static Results<TSuccess, NoContent, ProblemHttpResult> ToHttpResult<TData, TSuccess>(
        this Result<TData, HttpError> result, Func<TData, TSuccess> onSuccess)
        where TSuccess : IResult
    {
        if (result.IsSuccess)
            return result.Data is not null
                ? onSuccess(result.Data)
                : TypedResults.NoContent();

        return result.Error!.ToProblemResult();
    }

    /// <summary>
    /// Converts a <see cref="Result{HttpError}"/> (void operation with typed error) to an HTTP response.
    /// <list type="bullet">
    ///   <item>Success returns <c>204 No Content</c>.</item>
    ///   <item>Error returns an RFC 9457 Problem Details response derived from the <see cref="HttpError"/>.</item>
    /// </list>
    /// </summary>
    public static Results<NoContent, ProblemHttpResult> ToHttpResult(this Result<HttpError> result)
    {
        if (result.IsSuccess)
            return TypedResults.NoContent();

        return result.Error!.ToProblemResult();
    }
}
