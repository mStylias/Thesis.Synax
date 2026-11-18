namespace Synax.Mediator;

/// <summary>
/// Defines a handler for a request of type <typeparamref name="TRequest"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the request.
    /// </summary>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
