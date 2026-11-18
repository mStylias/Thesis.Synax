using System.ComponentModel;
using Synax.Results;

namespace Synax.Mediator;

/// <summary>
/// Internal interface for pipeline discovery. Do not implement directly;
/// implement <see cref="IWithValidation{TRequest, TValidation}"/> instead.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IValidatable<in TRequest>
{
    Task<Result> ValidateAsResultAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Implement on a handler to run validation that can short-circuit with an error result.
/// Return a success result to proceed to the handler, or an error result to short-circuit.
/// Use <see cref="Result{TError}"/> as <typeparamref name="TValidation"/> for validation
/// that does not produce data on success.
/// </summary>
public interface IWithValidation<in TRequest, TValidation> : IValidatable<TRequest>
    where TValidation : Result
{
    /// <summary>
    /// Validates the request. Return a success result to proceed to the handler,
    /// or an error result to short-circuit the pipeline.
    /// </summary>
    Task<TValidation> ValidateAsync(TRequest request, CancellationToken cancellationToken);

    async Task<Result> IValidatable<TRequest>.ValidateAsResultAsync(
        TRequest request, CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(request, cancellationToken);
        // Kept per handler instance so HandleAsync can read data-carrying validation
        // results via this.ValidationData() — see WithValidationExtensions.
        Internal.ValidationResultStore.Set(this, result);
        return result;
    }
}
