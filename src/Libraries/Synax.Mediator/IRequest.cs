namespace Synax.Mediator;

/// <summary>
/// Marker interface for a request that returns <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
// Deliberately invariant: the registry stores one pipeline per (request, response) pair,
// so a covariant send (e.g. SendAsync<object>) would fail at runtime with an invalid cast.
public interface IRequest<TResponse>;
