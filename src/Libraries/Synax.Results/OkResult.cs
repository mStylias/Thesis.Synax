namespace Synax.Results;

/// <summary>
/// Intermediate value returned by <see cref="Result.Ok()"/> and <see cref="Result.Warn()"/>.
/// Implicitly converts to any <c>Result</c> variant.
/// </summary>
public readonly struct OkResult
{
    internal ResultStatus Status { get; }

    internal OkResult(ResultStatus status) => Status = status;
}

/// <summary>
/// Intermediate value returned by <see cref="Result.Ok{TData}"/> and <see cref="Result.Warn{TData}"/>.
/// Implicitly converts to any <c>Result</c> variant whose data type matches <typeparamref name="TData"/>.
/// </summary>
public readonly struct OkResult<TData>
{
    internal TData Data { get; }
    internal ResultStatus Status { get; }

    internal OkResult(TData data, ResultStatus status)
    {
        Data = data;
        Status = status;
    }
}
