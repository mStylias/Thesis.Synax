using Synax.Mediator.Internal;
using Synax.Results;

namespace Synax.Mediator;

/// <summary>
/// Extensions for handlers whose <c>ValidateAsync</c> produces data the handler needs —
/// "parse, don't validate": fetch and check once in validation, consume the result in
/// <c>HandleAsync</c> via <see cref="ValidationData{TRequest,TState,TError}"/>.
/// </summary>
public static class WithValidationExtensions
{
    /// <summary>
    /// The data carried by this handler's successful validation result. Call from
    /// <c>HandleAsync</c> — the pipeline runs <c>ValidateAsync</c> first on the same instance
    /// and short-circuits on failure, so the state is present and validated by construction.
    /// <code>
    /// public async Task&lt;Result&lt;BookingResponse, HttpError&gt;&gt; HandleAsync(CreateBooking req, CancellationToken ct)
    /// {
    ///     var table = this.ValidationData();   // Table — fetched once, in ValidateAsync
    ///     ...
    /// }
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Validation has not run for this instance (e.g. the handler was invoked directly —
    /// use <see cref="ValidateAndHandleAsync{TRequest,TData,TError}"/> in tests), failed,
    /// or succeeded with null data.
    /// </exception>
    public static TState ValidationData<TRequest, TState, TError>(
        this IWithValidation<TRequest, Result<TState, TError>> handler)
    {
        var result = ValidationResultStore.Get(handler)
            ?? throw new InvalidOperationException(
                $"ValidateAsync has not run for this '{handler.GetType().Name}' instance. " +
                "When invoking a handler directly (e.g. in unit tests), use " +
                "handler.ValidateAndHandleAsync(request, ct) to mirror the pipeline's " +
                "validate-then-handle order.");

        if (result is not Result<TState, TError> typed)
            throw new InvalidOperationException(
                $"The stored validation result is a '{result.GetType().Name}', not a " +
                $"'Result<{typeof(TState).Name}, {typeof(TError).Name}>'.");

        if (typed.IsError)
            throw new InvalidOperationException(
                "ValidationData was read after a failed validation. The pipeline short-circuits " +
                "failed validations before HandleAsync; direct invocations must do the same.");

        return typed.Data
            ?? throw new InvalidOperationException(
                "The successful validation result carries null data. Return the validated state " +
                "from ValidateAsync when using ValidationData.");
    }

    /// <summary>
    /// Runs <c>ValidateAsync</c> then <c>HandleAsync</c> in pipeline order — for invoking a
    /// validated handler directly (unit tests, composition) without a mediator. A failed
    /// validation short-circuits into the response type exactly like the pipeline, and a
    /// successful one makes <c>this.ValidationData()</c> available to the handler.
    /// </summary>
    public static async Task<Result<TData, TError>> ValidateAndHandleAsync<TRequest, TData, TError>(
        this IRequestHandler<TRequest, Result<TData, TError>> handler,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<Result<TData, TError>>
    {
        if (handler is IValidatable<TRequest> validatable)
        {
            var validation = await validatable.ValidateAsResultAsync(request, cancellationToken);
            if (validation.IsError)
            {
                return validation switch
                {
                    Result<TData, TError> sameShape => sameShape,
                    Result<TError> voidShape => Result<TData, TError>.FromError(voidShape),
                    _ when validation.ErrorObject is TError typedError
                        => Result<TData, TError>.FromError(validation, typedError),
                    _ => throw new InvalidOperationException(
                        $"Validation of '{handler.GetType().Name}' failed with error type " +
                        $"'{validation.ErrorObject?.GetType().Name ?? "null"}', which does not match " +
                        $"the response error type '{typeof(TError).Name}'.")
                };
            }
        }

        return await handler.HandleAsync(request, cancellationToken);
    }
}
