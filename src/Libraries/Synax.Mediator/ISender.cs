namespace Synax.Mediator;

/// <summary>
/// Dispatches a request to its corresponding handler and returns the response.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Sends a request to its handler.
    /// </summary>
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
