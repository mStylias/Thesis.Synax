using System.Runtime.CompilerServices;
using Synax.Results;

namespace Synax.AspNetCore.Errors;

/// <summary>
/// Accumulating validation context for request handlers: every field rule evaluates, and
/// together they produce a single <c>400 VALIDATION_FAILED</c> carrying all field errors —
/// what form UIs need to mark every invalid field at once.
/// <para>
/// The context only covers field-shape errors. Entity/state checks (missing, duplicate,
/// capacity — anything touching the database) use ordinary control flow with the
/// <see cref="HttpError"/> factories, so each failure keeps its own status and error code:
/// </para>
/// <code>
/// var v = new Validation("The table request is invalid.")
///     .Require(request.Number > 0, "TABLE_INVALID_NUMBER",
///         "Must be greater than zero.", nameof(request.Number))
///     .Require(request.Seats > 0, "TABLE_INVALID_SEATS",
///         "Must be greater than zero.", nameof(request.Seats));
/// if (v.HasErrors) return v.ToResult();
///
/// var exists = await _restaurantRepo.Query(q => q.Where(r => r.Id == request.RestaurantId))
///     .AnyAsync(ct);
/// if (!exists) return HttpError.NotFound("The specified restaurant was not found.");
///
/// return Result.Ok();
/// </code>
/// Not thread-safe.
/// </summary>
public sealed class Validation(string? description = null)
{
    private readonly List<ValidationError> _fieldErrors = [];
    private ResultOrigin? _firstFieldOrigin;

    /// <summary>
    /// <c>true</c> when any rule has failed. Read <see cref="Error"/> (or call
    /// <see cref="ToResult()"/>) to obtain the response.
    /// </summary>
    public bool HasErrors => _fieldErrors.Count > 0;

    /// <summary>
    /// The accumulated field errors as one <c>400 VALIDATION_FAILED</c>
    /// (origin = the first failed rule's call site).
    /// Throws when nothing has failed — check <see cref="HasErrors"/> first.
    /// </summary>
    public HttpError Error
    {
        get
        {
            if (_fieldErrors.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No validation errors have been recorded — check {nameof(HasErrors)} before reading {nameof(Error)}.");
            }

            var error = HttpError.Validation(
                description ?? "The request is invalid.", validationErrors: [.. _fieldErrors]);
            error.ResultOrigin = _firstFieldOrigin;
            return error;
        }
    }

    /// <summary>
    /// Records a <see cref="ValidationError"/> when <paramref name="condition"/> is
    /// <c>false</c>. Rules accumulate — all of them evaluate and report together.
    /// The trailing caller-info parameters stamp the aggregate's <see cref="ResultOrigin"/>
    /// from the first failing rule — never pass them explicitly.
    /// </summary>
    public Validation Require(
        bool condition, string code, string description, string? propertyName = null,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
    {
        if (condition) return this;

        _firstFieldOrigin ??= new ResultOrigin(originMember, originFile, originLine);
        _fieldErrors.Add(new ValidationError(code, description, propertyName));
        return this;
    }

    /// <summary>
    /// Records <paramref name="error"/> unconditionally — the escape hatch for checks that
    /// don't fit a boolean expression (nested <see cref="ValidationError.InnerErrors"/>,
    /// collection indexer paths).
    /// </summary>
    public Validation Add(
        ValidationError error,
        [CallerMemberName] string originMember = "", [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
    {
        _firstFieldOrigin ??= new ResultOrigin(originMember, originFile, originLine);
        _fieldErrors.Add(error);
        return this;
    }

    /// <summary>
    /// Terminates the context as a void result: <see cref="Result.Ok()"/> when nothing failed,
    /// otherwise a failure carrying <see cref="Error"/> (origin transfers from the error).
    /// </summary>
    public Result<HttpError> ToResult()
    {
        if (HasErrors) return Error;
        return Result.Ok();
    }

    /// <summary>
    /// Terminates the context as a data-carrying result — the "parse, don't validate" shape for
    /// <c>IWithValidation</c>: <paramref name="data"/> on success (read back in the handler via
    /// <c>this.ValidationData()</c>), otherwise a failure carrying <see cref="Error"/>.
    /// </summary>
    public Result<TData, HttpError> ToResult<TData>(TData data)
    {
        if (HasErrors) return Error;
        return Result.Ok<TData, HttpError>(data);
    }
}
