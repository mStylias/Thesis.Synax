namespace Synax.Mediator;

/// <summary>
/// Marker for a query that returns <typeparamref name="TResponse"/>.
/// </summary>
public interface IQuery<TResponse> : IRequest<TResponse>;
