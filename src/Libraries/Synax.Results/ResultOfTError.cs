using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Synax.Results;

/// <summary>
/// Represents the outcome of a void operation that carries a typed <typeparamref name="TError"/> on failure
/// but produces no data on success.
/// </summary>
public class Result<TError> : Result
{
    /// <summary>
    /// The typed error describing why the operation failed. Only meaningful when <see cref="IsError"/> is <c>true</c>.
    /// </summary>
    public TError? Error { get; }

    /// <inheritdoc/>
    public override object? ErrorObject => Error;

    internal Result(TError? error, ResultStatus status,
        ResultOrigin? origin = null, Exception? exception = null)
        : base(status, origin, exception)
    {
        Error = error;
    }

    /// <inheritdoc cref="Result.IsError"/>
    [MemberNotNullWhen(true, nameof(Error))]
    public new bool IsError => base.IsError;

    /// <inheritdoc cref="Result.AddMessage"/>
    public new Result<TError> AddMessage(string message)
    {
        AddMessageInternal(message);
        return this;
    }

    // -------- Combinators --------

    /// <summary>
    /// Chains a data-producing fallible operation on a successful result and flattens the outcome.
    /// A failure passes through unchanged and <paramref name="binder"/> is not invoked.
    /// The bound result's status wins; this result's messages are prepended to it.
    /// </summary>
    public Result<TData, TError> Bind<TData>(Func<Result<TData, TError>> binder)
    {
        if (IsError) return PassErrorTo<TData>();

        var next = binder();
        if (Messages.Count == 0) return next;

        var merged = new Result<TData, TError>(next.Data, next.Error, next.Status, next.Origin, next.Exception);
        merged.AddMessagesInternal(Messages);
        merged.AddMessagesInternal(next.Messages);
        return merged;
    }

    /// <summary>
    /// Chains a further void fallible operation on a successful result and flattens the outcome.
    /// A failure passes through unchanged and <paramref name="binder"/> is not invoked.
    /// The bound result's status wins; this result's messages are prepended to it.
    /// </summary>
    public Result<TError> Bind(Func<Result<TError>> binder)
    {
        if (IsError) return this;

        var next = binder();
        if (Messages.Count == 0) return next;

        var merged = new Result<TError>(next.Error, next.Status, next.Origin, next.Exception);
        merged.AddMessagesInternal(Messages);
        merged.AddMessagesInternal(next.Messages);
        return merged;
    }

    /// <summary>
    /// Transforms the error of a failed result with <paramref name="mapper"/>.
    /// A success passes through unchanged (status and messages are preserved).
    /// </summary>
    public Result<TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
    {
        var result = IsError
            ? new Result<TNewError>(mapper(Error), ResultStatus.Error, Origin, Exception)
            : new Result<TNewError>(default, Status, Origin, Exception);
        result.AddMessagesInternal(Messages);
        return result;
    }

    /// <summary>
    /// Fails a successful result when <paramref name="predicate"/> is not satisfied,
    /// using <paramref name="errorFactory"/> to build the error. Source location is captured automatically.
    /// A failure passes through unchanged and the predicate is not evaluated.
    /// </summary>
    public Result<TError> Ensure(
        Func<bool> predicate,
        Func<TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (IsError || predicate()) return this;

        var failure = new Result<TError>(errorFactory(), ResultStatus.Error,
            new ResultOrigin(memberName, filePath, lineNumber), Exception);
        failure.AddMessagesInternal(Messages);
        return failure;
    }

    /// <summary>
    /// Runs a side effect when the result is successful. The result is returned unchanged.
    /// Exceptions thrown by <paramref name="action"/> propagate to the caller.
    /// </summary>
    public Result<TError> Tap(Action action)
    {
        if (IsSuccess) action();
        return this;
    }

    /// <summary>
    /// Runs a side effect on the error of a failed result. The result is returned unchanged.
    /// Exceptions thrown by <paramref name="action"/> propagate to the caller.
    /// </summary>
    public Result<TError> TapError(Action<TError> action)
    {
        if (IsError) action(Error);
        return this;
    }

    /// <summary>
    /// Resolves the result into a plain value by handling both tracks:
    /// <paramref name="onSuccess"/> runs for successes (also warnings), <paramref name="onError"/> receives the error.
    /// </summary>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<TError, TResult> onError)
        => IsError ? onError(Error) : onSuccess();

    /// <summary>
    /// Re-types a failed result to a data-carrying result, preserving error, origin, exception, and messages.
    /// </summary>
    internal Result<TData, TError> PassErrorTo<TData>()
    {
        var result = new Result<TData, TError>(default, Error, ResultStatus.Error, Origin, Exception);
        result.AddMessagesInternal(Messages);
        return result;
    }

    // -------- Implicit conversions --------

    /// <summary>Converts a parameterless <see cref="Result.Ok()"/> or <see cref="Result.Warn()"/>.</summary>
    public static implicit operator Result<TError>(OkResult ok)
        => new(default, ok.Status);

    /// <summary>
    /// Wraps <paramref name="error"/> in a failure result — the terse form of
    /// <c>Result.Fail(error)</c>. No stack walking: the origin transfers from the error
    /// when it implements <see cref="IHasResultOrigin"/> (error factories capture it via
    /// caller info at creation), and is absent otherwise.
    /// </summary>
    public static implicit operator Result<TError>(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TError>(error, ResultStatus.Error,
            (error as IHasResultOrigin)?.ResultOrigin);
    }

    /// <summary>Converts a typed <see cref="Result.Fail{TError}"/>.</summary>
    public static implicit operator Result<TError>(FailResult<TError> fail)
    {
        var result = new Result<TError>(fail.Error, ResultStatus.Error, fail.Origin, fail.Exception);
        if (fail.Messages is not null)
            foreach (var msg in fail.Messages) result.AddMessageInternal(msg);
        return result;
    }
}
