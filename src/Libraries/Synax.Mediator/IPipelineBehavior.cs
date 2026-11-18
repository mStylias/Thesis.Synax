namespace Synax.Mediator;

/// <summary>
/// Delegate representing the next step in the pipeline (either another behavior or the handler).
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Defines a behavior that wraps request handling, allowing cross-cutting logic before and after the handler.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Executes behavior logic. Call <paramref name="next"/> to continue the pipeline.
    /// </summary>
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
