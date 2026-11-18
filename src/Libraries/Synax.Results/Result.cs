using System.Runtime.CompilerServices;

namespace Synax.Results;

/// <summary>
/// Represents the outcome of an operation that produces no data.
/// Also serves as the entry point for all static factory methods:
/// <see cref="Ok()"/>, <see cref="Fail"/>, <see cref="Ok{TData}"/>, <see cref="Fail{TError}"/>.
/// </summary>
public class Result
{
    private readonly List<string> _messages = [];

    /// <summary>
    /// The outcome status of this result.
    /// </summary>
    public ResultStatus Status { get; }

    /// <summary>
    /// <c>true</c> when the operation succeeded or completed with warnings.
    /// </summary>
    public bool IsSuccess => Status != ResultStatus.Error;

    /// <summary>
    /// <c>true</c> when the operation completed with warnings.
    /// </summary>
    public bool IsWarning => Status == ResultStatus.Warning;

    /// <summary>
    /// <c>true</c> when the operation failed.
    /// </summary>
    public bool IsError => Status == ResultStatus.Error;

    /// <summary>
    /// An optional exception attached to this result (e.g. from third-party code).
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// The source location where this failure originated.
    /// Populated automatically when using <see cref="Fail"/> or <see cref="Fail{TError}"/>.
    /// </summary>
    public ResultOrigin? Origin { get; }

    /// <summary>
    /// Log messages accumulated as this result passes through successive layers.
    /// </summary>
    public IReadOnlyList<string> Messages => _messages;

    /// <summary>
    /// The typed error carried by derived results, untyped — lets generic consumers (telemetry,
    /// logging) inspect the error without knowing <c>TError</c>, e.g. via <see cref="IHasErrorCode"/>.
    /// <c>null</c> on the non-generic <see cref="Result"/> and on non-failed results.
    /// </summary>
    public virtual object? ErrorObject => null;

    private protected Result(ResultStatus status, ResultOrigin? origin = null, Exception? exception = null)
    {
        Status = status;
        Origin = origin;
        Exception = exception;
    }

    internal void AddMessageInternal(string message) => _messages.Add(message);

    internal void AddMessagesInternal(IReadOnlyList<string> messages) => _messages.AddRange(messages);

    /// <summary>
    /// Appends a log message. Messages accumulate as the result bubbles through layers.
    /// </summary>
    public Result AddMessage(string message)
    {
        _messages.Add(message);
        return this;
    }

    // -------- Static factory methods --------

    /// <summary>Creates a successful result with no data.</summary>
    public static OkResult Ok() => new(ResultStatus.Success);

    /// <summary>Creates a successful result carrying <paramref name="data"/>.</summary>
    public static OkResult<TData> Ok<TData>(TData data) => new(data, ResultStatus.Success);

    /// <summary>Creates a warning result with no data.</summary>
    public static OkResult Warn() => new(ResultStatus.Warning);

    /// <summary>Creates a warning result carrying <paramref name="data"/>.</summary>
    public static OkResult<TData> Warn<TData>(TData data) => new(data, ResultStatus.Warning);

    /// <summary>
    /// Creates a failure result. Source location is captured automatically.
    /// </summary>
    public static FailResult Fail(
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => new(new ResultOrigin(memberName, filePath, lineNumber));

    /// <summary>
    /// Creates a failure result carrying a typed <paramref name="error"/>.
    /// Source location is captured automatically.
    /// </summary>
    /// <remarks>
    /// This overload takes priority over <see cref="Fail"/> so that a <see cref="string"/> argument
    /// becomes a typed error instead of silently binding to <see cref="Fail"/>'s caller-info parameters.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static FailResult<TError> Fail<TError>(
        TError error,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => new(error, new ResultOrigin(memberName, filePath, lineNumber));

    // -------- Two-type-parameter factory methods --------

    /// <summary>Creates a successful <see cref="Result{TData,TError}"/> carrying <paramref name="data"/>.</summary>
    public static Result<TData, TError> Ok<TData, TError>(TData data)
        => new(data, default, ResultStatus.Success);

    /// <summary>Creates a warning <see cref="Result{TData,TError}"/> carrying <paramref name="data"/>.</summary>
    public static Result<TData, TError> Warn<TData, TError>(TData data)
        => new(data, default, ResultStatus.Warning);

    /// <summary>
    /// Creates a failed <see cref="Result{TData,TError}"/> carrying a typed <paramref name="error"/>.
    /// Source location is captured automatically.
    /// </summary>
    public static Result<TData, TError> Fail<TData, TError>(
        TError error,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => new(default, error, ResultStatus.Error, new ResultOrigin(memberName, filePath, lineNumber));

    // -------- Try factory methods --------
    // All Try variants catch every exception EXCEPT OperationCanceledException (and derived),
    // which propagates: cancellation is a host-level control-flow signal, not a failure.

    /// <summary>
    /// Executes <paramref name="action"/> and returns a successful result, or a failed result
    /// carrying the caught exception. Source location is captured automatically.
    /// </summary>
    public static Result Try(
        Action action,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            action();
            return new Result(ResultStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Result(ResultStatus.Error, new ResultOrigin(memberName, filePath, lineNumber), ex);
        }
    }

    /// <summary>
    /// Executes <paramref name="action"/> and returns a successful result, or a failed result
    /// with a typed error built by <paramref name="errorFactory"/> and the caught exception attached.
    /// Source location is captured automatically.
    /// </summary>
    public static Result<TError> Try<TError>(
        Action action,
        Func<Exception, TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            action();
            return new Result<TError>(default, ResultStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Result<TError>(errorFactory(ex), ResultStatus.Error,
                new ResultOrigin(memberName, filePath, lineNumber), ex);
        }
    }

    /// <summary>
    /// Executes <paramref name="func"/> and returns its value in a successful result, or a failed result
    /// with a typed error built by <paramref name="errorFactory"/> and the caught exception attached.
    /// Source location is captured automatically.
    /// </summary>
    public static Result<TData, TError> Try<TData, TError>(
        Func<TData> func,
        Func<Exception, TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            return new Result<TData, TError>(func(), default, ResultStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Result<TData, TError>(default, errorFactory(ex), ResultStatus.Error,
                new ResultOrigin(memberName, filePath, lineNumber), ex);
        }
    }

    /// <inheritdoc cref="Try(Action, string, string, int)"/>
    public static async Task<Result> TryAsync(
        Func<Task> action,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            await action().ConfigureAwait(false);
            return new Result(ResultStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Result(ResultStatus.Error, new ResultOrigin(memberName, filePath, lineNumber), ex);
        }
    }

    /// <inheritdoc cref="Try{TError}(Action, Func{Exception, TError}, string, string, int)"/>
    public static async Task<Result<TError>> TryAsync<TError>(
        Func<Task> action,
        Func<Exception, TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            await action().ConfigureAwait(false);
            return new Result<TError>(default, ResultStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Result<TError>(errorFactory(ex), ResultStatus.Error,
                new ResultOrigin(memberName, filePath, lineNumber), ex);
        }
    }

    /// <inheritdoc cref="Try{TData, TError}(Func{TData}, Func{Exception, TError}, string, string, int)"/>
    public static async Task<Result<TData, TError>> TryAsync<TData, TError>(
        Func<Task<TData>> func,
        Func<Exception, TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            return new Result<TData, TError>(await func().ConfigureAwait(false), default, ResultStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Result<TData, TError>(default, errorFactory(ex), ResultStatus.Error,
                new ResultOrigin(memberName, filePath, lineNumber), ex);
        }
    }

    // -------- Implicit conversions from intermediates --------

    public static implicit operator Result(OkResult ok) => new(ok.Status);

    public static implicit operator Result(FailResult fail)
    {
        var result = new Result(ResultStatus.Error, fail.Origin, fail.Exception);
        if (fail.Messages is not null) result._messages.AddRange(fail.Messages);
        return result;
    }
}
