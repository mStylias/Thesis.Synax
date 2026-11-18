namespace Synax.Mediator;

/// <summary>
/// Handler for a query that returns <typeparamref name="TResponse"/>.
/// </summary>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
}
