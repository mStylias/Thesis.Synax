using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Synax.AspNetCore.Errors;

/// <summary>
/// Extension methods for converting <see cref="HttpError"/> to Problem Details responses.
/// </summary>
public static class HttpErrorExtensions
{
    /// <summary>
    /// Converts an <see cref="HttpError"/> to an RFC 9457 Problem Details <see cref="ProblemHttpResult"/>.
    /// An unset or invalid <see cref="HttpError.HttpCode"/> (below 100) falls back to <c>500</c>.
    /// Validation errors are emitted under the <c>errors</c> extension with their
    /// <see cref="ValidationError.PropertyName"/>s camelCased to match the request JSON.
    /// </summary>
    public static ProblemHttpResult ToProblemResult(this HttpError error)
    {
        Dictionary<string, object?>? extensions = null;

        if (error.ValidationErrors is { Count: > 0 })
            extensions = new() { ["errors"] = error.ValidationErrors.Select(CamelCaseProperty).ToList() };

        return TypedResults.Problem(
            title: error.ErrorCode,
            detail: error.Description,
            statusCode: error.HttpCode >= 100 ? error.HttpCode : StatusCodes.Status500InternalServerError,
            extensions: extensions);
    }

    /// <summary>
    /// CamelCases each dotted path segment of <see cref="ValidationError.PropertyName"/>
    /// ("Address.Street" → "address.street", indexers preserved), recursing into inner errors,
    /// so clients can match the name against the request JSON they sent.
    /// </summary>
    private static ValidationError CamelCaseProperty(ValidationError error) => error with
    {
        PropertyName = error.PropertyName is null
            ? null
            : string.Join('.', error.PropertyName.Split('.')
                .Select(JsonNamingPolicy.CamelCase.ConvertName)),
        InnerErrors = error.InnerErrors is { Count: > 0 }
            ? error.InnerErrors.Select(CamelCaseProperty).ToList()
            : error.InnerErrors
    };
}
