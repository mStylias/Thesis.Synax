namespace Synax.Results;

/// <summary>
/// Intermediate value returned by <see cref="Result.Fail"/>.
/// Implicitly converts to a plain <see cref="Result"/>, carrying origin, exception, and initial messages.
/// It carries no error value, so it cannot convert to <see cref="Result{TError}"/> or
/// <see cref="Result{TData,TError}"/> — those require a typed error via <see cref="Result.Fail{TError}"/>.
/// </summary>
public readonly struct FailResult
{
    // Nullable because a default(FailResult) bypasses the internal constructor.
    internal ResultOrigin? Origin { get; }
    internal Exception? Exception { get; }
    internal IReadOnlyList<string>? Messages { get; }

    internal FailResult(ResultOrigin origin, Exception? exception = null, IReadOnlyList<string>? messages = null)
    {
        Origin = origin;
        Exception = exception;
        Messages = messages;
    }

    /// <summary>
    /// Attaches a caught exception to this failure.
    /// </summary>
    public FailResult WithException(Exception exception) => new(Origin!, exception, Messages);

    /// <summary>
    /// Appends a log message to this failure. Messages accumulate across repeated calls.
    /// </summary>
    public FailResult WithMessage(string message) => new(Origin!, Exception, [.. Messages ?? [], message]);
}

/// <summary>
/// Intermediate value returned by <see cref="Result.Fail{TError}"/>.
/// Implicitly converts to <see cref="Result{TError}"/> or <see cref="Result{TData, TError}"/>,
/// carrying a typed error, origin, exception, and initial messages.
/// </summary>
public readonly struct FailResult<TError>
{
    internal TError Error { get; }
    // Nullable because a default(FailResult<TError>) bypasses the internal constructor.
    internal ResultOrigin? Origin { get; }
    internal Exception? Exception { get; }
    internal IReadOnlyList<string>? Messages { get; }

    internal FailResult(TError error, ResultOrigin origin, Exception? exception = null,
        IReadOnlyList<string>? messages = null)
    {
        Error = error;
        Origin = origin;
        Exception = exception;
        Messages = messages;
    }

    /// <summary>
    /// Attaches a caught exception to this failure.
    /// </summary>
    public FailResult<TError> WithException(Exception exception) => new(Error, Origin!, exception, Messages);

    /// <summary>
    /// Appends a log message to this failure. Messages accumulate across repeated calls.
    /// </summary>
    public FailResult<TError> WithMessage(string message)
        => new(Error, Origin!, Exception, [.. Messages ?? [], message]);
}
