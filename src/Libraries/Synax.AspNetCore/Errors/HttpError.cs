using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Synax.Results;

namespace Synax.AspNetCore.Errors;

public class HttpError : IHasErrorCode, IHasResultOrigin
{
    public string ErrorCode { get; set; } = string.Empty;
    public int HttpCode { get; set; }
    public string? Description { get; set; }
    public ICollection<ValidationError>? ValidationErrors { get; set; }

    /// <summary>
    /// Where this error was created — stamped by the factories via caller info (zero runtime
    /// cost) and transferred to the failed <c>Result</c> by the implicit error conversion.
    /// Server-side diagnostics only; never serialized to clients.
    /// </summary>
    [JsonIgnore]
    public ResultOrigin? ResultOrigin { get; set; }

    // ---------- Factories ---------- //
    // Each factory uses a conventional default ErrorCode; pass errorCode to use a
    // domain-specific one, e.g. HttpError.NotFound("...", errorCode: "RESTAURANT_NOT_FOUND").
    // The trailing caller-info parameters stamp ResultOrigin — never pass them explicitly.

    public static HttpError BadRequest(string? description = null, string? errorCode = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
        => Create(400, errorCode ?? "BAD_REQUEST", description, originMember, originFile, originLine);

    public static HttpError Unauthorized(string? description = null, string? errorCode = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
        => Create(401, errorCode ?? "UNAUTHORIZED", description, originMember, originFile, originLine);

    public static HttpError Forbidden(string? description = null, string? errorCode = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
        => Create(403, errorCode ?? "FORBIDDEN", description, originMember, originFile, originLine);

    public static HttpError NotFound(string? description = null, string? errorCode = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
        => Create(404, errorCode ?? "NOT_FOUND", description, originMember, originFile, originLine);

    public static HttpError Conflict(string? description = null, string? errorCode = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
        => Create(409, errorCode ?? "CONFLICT", description, originMember, originFile, originLine);

    public static HttpError UnprocessableEntity(string? description = null, string? errorCode = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
        => Create(422, errorCode ?? "UNPROCESSABLE_ENTITY", description, originMember, originFile, originLine);

    public static HttpError Internal(string? description = null, string? errorCode = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
        => Create(500, errorCode ?? "INTERNAL_ERROR", description, originMember, originFile, originLine);

    /// <summary>
    /// A <c>400</c> validation failure carrying field-level <see cref="ValidationError"/>s.
    /// (An array parameter rather than <c>params</c> so the caller-info origin stamping can
    /// follow it — collection expressions keep call sites terse: <c>[new ValidationError(...)]</c>.)
    /// </summary>
    public static HttpError Validation(
        string? description = null, string? errorCode = null, ValidationError[]? validationErrors = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
    {
        var error = Create(400, errorCode ?? "VALIDATION_FAILED", description, originMember, originFile, originLine);
        error.ValidationErrors = validationErrors is { Length: > 0 } ? validationErrors : null;
        return error;
    }

    private static HttpError Create(
        int httpCode, string errorCode, string? description,
        string originMember, string originFile, int originLine)
        => new()
        {
            ErrorCode = errorCode,
            HttpCode = httpCode,
            Description = description,
            ResultOrigin = new ResultOrigin(originMember, originFile, originLine)
        };

}
