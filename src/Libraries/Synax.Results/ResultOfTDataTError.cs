using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Synax.Results;

/// <summary>
/// Represents the outcome of an operation that produces <typeparamref name="TData"/> on success
/// or <typeparamref name="TError"/> on failure.
/// </summary>
public class Result<TData, TError> : Result
{
    /// <summary>
    /// The data produced by a successful operation. Only meaningful when <see cref="IsSuccess"/> is <c>true</c>.
    /// </summary>
    public TData? Data { get; }

    /// <summary>
    /// The typed error describing why the operation failed. Only meaningful when <see cref="IsError"/> is <c>true</c>.
    /// </summary>
    public TError? Error { get; }

    /// <inheritdoc/>
    public override object? ErrorObject => Error;

    internal Result(TData? data, TError? error, ResultStatus status,
        ResultOrigin? origin = null, Exception? exception = null)
        : base(status, origin, exception)
    {
        Data = data;
        Error = error;
    }

    /// <inheritdoc cref="Result.IsSuccess"/>
    [MemberNotNullWhen(true, nameof(Data))]
    public new bool IsSuccess => base.IsSuccess;

    /// <inheritdoc cref="Result.IsError"/>
    [MemberNotNullWhen(true, nameof(Error))]
    public new bool IsError => base.IsError;

    /// <inheritdoc cref="Result.AddMessage"/>
    public new Result<TData, TError> AddMessage(string message)
    {
        AddMessageInternal(message);
        return this;
    }

    /// <summary>
    /// Creates an error <see cref="Result{TData,TError}"/> from an error result of any shape,
    /// carrying the given typed <paramref name="error"/> and preserving the source's origin,
    /// exception, and messages. Used when the source's data type parameter is not statically known.
    /// </summary>
    /// <exception cref="InvalidOperationException">The source result is not an error.</exception>
    public static Result<TData, TError> FromError(Result source, TError error)
    {
        if (!source.IsError)
            throw new InvalidOperationException(
                $"Cannot convert a non-error result to Result<{typeof(TData).Name}, {typeof(TError).Name}>.");

        var result = new Result<TData, TError>(default, error, ResultStatus.Error, source.Origin, source.Exception);
        result.AddMessagesInternal(source.Messages);
        return result;
    }

    /// <summary>
    /// Creates an error <see cref="Result{TData,TError}"/> from an error result carrying the
    /// same error type but different data, preserving the error, origin, exception, and messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">The source result is not an error.</exception>
    public static Result<TData, TError> FromError<TOtherData>(Result<TOtherData, TError> source)
        => FromError(source, source.IsError
            ? source.Error
            : throw new InvalidOperationException(
                $"Cannot convert a non-error result to Result<{typeof(TData).Name}, {typeof(TError).Name}>."));

    /// <summary>
    /// Creates an error <see cref="Result{TData,TError}"/> from an error <see cref="Result{TError}"/>,
    /// preserving the original error, origin, exception, and messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">The source result is not an error.</exception>
    public static Result<TData, TError> FromError(Result<TError> source)
    {
        if (!source.IsError)
            throw new InvalidOperationException(
                $"Cannot convert a non-error Result<{typeof(TError).Name}> to Result<{typeof(TData).Name}, {typeof(TError).Name}>.");

        var result = new Result<TData, TError>(default, source.Error, source.Status, source.Origin, source.Exception);
        foreach (var msg in source.Messages) result.AddMessageInternal(msg);
        return result;
    }

    // -------- Combinators --------

    /// <summary>
    /// Transforms the data of a successful result with <paramref name="mapper"/>.
    /// A failure passes through unchanged (error, origin, exception, and messages are preserved).
    /// </summary>
    public Result<TNew, TError> Map<TNew>(Func<TData, TNew> mapper)
    {
        if (IsError) return PassErrorTo<TNew>();

        var result = new Result<TNew, TError>(mapper(Data!), default, Status, Origin, Exception);
        result.AddMessagesInternal(Messages);
        return result;
    }

    /// <summary>
    /// Chains a further fallible operation on a successful result and flattens the outcome.
    /// A failure passes through unchanged and <paramref name="binder"/> is not invoked.
    /// The bound result's status wins; this result's messages are prepended to it.
    /// </summary>
    public Result<TNew, TError> Bind<TNew>(Func<TData, Result<TNew, TError>> binder)
    {
        if (IsError) return PassErrorTo<TNew>();

        var next = binder(Data!);
        if (Messages.Count == 0) return next;

        var merged = new Result<TNew, TError>(next.Data, next.Error, next.Status, next.Origin, next.Exception);
        merged.AddMessagesInternal(Messages);
        merged.AddMessagesInternal(next.Messages);
        return merged;
    }

    /// <summary>
    /// Transforms the error of a failed result with <paramref name="mapper"/>.
    /// A success passes through unchanged (data, status, and messages are preserved).
    /// </summary>
    public Result<TData, TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
    {
        var result = IsError
            ? new Result<TData, TNewError>(default, mapper(Error), ResultStatus.Error, Origin, Exception)
            : new Result<TData, TNewError>(Data, default, Status, Origin, Exception);
        result.AddMessagesInternal(Messages);
        return result;
    }

    /// <summary>
    /// Fails a successful result when <paramref name="predicate"/> is not satisfied by its data,
    /// using <paramref name="errorFactory"/> to build the error. Source location is captured automatically.
    /// A failure passes through unchanged and the predicate is not evaluated.
    /// </summary>
    public Result<TData, TError> Ensure(
        Func<TData, bool> predicate,
        Func<TData, TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (IsError || predicate(Data!)) return this;

        var failure = new Result<TData, TError>(default, errorFactory(Data!), ResultStatus.Error,
            new ResultOrigin(memberName, filePath, lineNumber), Exception);
        failure.AddMessagesInternal(Messages);
        return failure;
    }

    /// <summary>
    /// Runs a side effect on the data of a successful result. The result is returned unchanged.
    /// Exceptions thrown by <paramref name="action"/> propagate to the caller.
    /// </summary>
    public Result<TData, TError> Tap(Action<TData> action)
    {
        if (IsSuccess) action(Data);
        return this;
    }

    /// <summary>
    /// Runs a side effect on the error of a failed result. The result is returned unchanged.
    /// Exceptions thrown by <paramref name="action"/> propagate to the caller.
    /// </summary>
    public Result<TData, TError> TapError(Action<TError> action)
    {
        if (IsError) action(Error);
        return this;
    }

    /// <summary>
    /// Resolves the result into a plain value by handling both tracks:
    /// <paramref name="onSuccess"/> receives the data (also for warnings), <paramref name="onError"/> the error.
    /// </summary>
    public TResult Match<TResult>(Func<TData, TResult> onSuccess, Func<TError, TResult> onError)
        => IsError ? onError(Error) : onSuccess(Data!);

    /// <summary>
    /// Re-types a failed result to a different data type, preserving error, origin, exception, and messages.
    /// </summary>
    internal Result<TNew, TError> PassErrorTo<TNew>()
    {
        var result = new Result<TNew, TError>(default, Error, ResultStatus.Error, Origin, Exception);
        result.AddMessagesInternal(Messages);
        return result;
    }

    // -------- Implicit conversions --------

    /// <summary>Wraps <paramref name="data"/> in a successful result.</summary>
    public static implicit operator Result<TData, TError>(TData data)
        => new(data, default, ResultStatus.Success);

    /// <summary>
    /// Wraps <paramref name="error"/> in a failure result — the terse form of
    /// <c>Result.Fail(error)</c>. No stack walking: the origin transfers from the error
    /// when it implements <see cref="IHasResultOrigin"/> (error factories capture it via
    /// caller info at creation), and is absent otherwise.
    /// </summary>
    public static implicit operator Result<TData, TError>(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TData, TError>(default, error, ResultStatus.Error,
            (error as IHasResultOrigin)?.ResultOrigin);
    }

    /// <summary>Converts an <see cref="Result.Ok{TData}"/> or <see cref="Result.Warn{TData}"/>.</summary>
    public static implicit operator Result<TData, TError>(OkResult<TData> ok)
        => new(ok.Data, default, ok.Status);

    /// <summary>Converts a typed <see cref="Result.Fail{TError}"/>.</summary>
    public static implicit operator Result<TData, TError>(FailResult<TError> fail)
    {
        var result = new Result<TData, TError>(default, fail.Error, ResultStatus.Error, fail.Origin, fail.Exception);
        if (fail.Messages is not null)
            foreach (var msg in fail.Messages) result.AddMessageInternal(msg);
        return result;
    }
}
