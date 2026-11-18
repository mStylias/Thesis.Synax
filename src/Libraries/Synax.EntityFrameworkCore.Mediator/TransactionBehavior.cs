using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Pipeline behavior that wraps the handler in a unit-of-work transaction:
/// commits when the handler returns normally (or with a successful <see cref="Result"/>),
/// rolls back when it returns a failed <see cref="Result"/> or throws (the exception is rethrown).
/// <para>
/// Subclass it with your context to get a 2-arity type the mediator can attach:
/// <code>
/// public sealed class AppTransactionBehavior&lt;TRequest, TResponse&gt;(IUnitOfWork&lt;AppContext&gt; uow)
///     : TransactionBehavior&lt;AppContext, TRequest, TResponse&gt;(uow)
///     where TRequest : IRequest&lt;TResponse&gt;;
/// </code>
/// then attach per handler via <c>[MediatorBehavior(typeof(AppTransactionBehavior&lt;,&gt;))]</c> or
/// scope it globally via <c>AddGlobalBehavior(typeof(AppTransactionBehavior&lt;,&gt;), appliesTo: ...)</c>.
/// </para>
/// <para>
/// Safe to nest: when a transaction is already active on the context, the inner scope joins it —
/// its commit is a no-op and only the outermost scope decides the final outcome (an inner rollback
/// still propagates to the real transaction).
/// </para>
/// </summary>
public abstract class TransactionBehavior<TContext, TRequest, TResponse>(IUnitOfWork<TContext> unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TContext : ISynaxContext
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();

            if (response is Result { IsError: true })
                await transaction.RollbackAsync(CancellationToken.None);
            else
                await transaction.CommitAsync(cancellationToken);

            return response;
        }
        catch
        {
            // CancellationToken.None: the rollback must run even when the failure
            // that got us here is the cancellation itself.
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
