namespace Synax.Mediator;

/// <summary>
/// Marker for a command that returns no result.
/// </summary>
public interface ICommand : IRequest<Unit>;

/// <summary>
/// Marker for a command that returns <typeparamref name="TResponse"/>.
/// </summary>
public interface ICommand<TResponse> : IRequest<TResponse>;
