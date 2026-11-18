using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Behaviors;

/// <summary>
/// The library's <see cref="TransactionBehavior{TContext,TRequest,TResponse}"/> closed over our
/// context tag (2-arity, so it can be attached via <c>[MediatorBehavior]</c>). Commits when the
/// handler returns a successful <c>Result</c>, rolls back on a failed one or an exception.
/// Nesting-safe: an inner scope joins the outer transaction.
/// </summary>
public sealed class BookingTransactionBehavior<TRequest, TResponse>(IUnitOfWork<BookingContext> unitOfWork)
    : TransactionBehavior<BookingContext, TRequest, TResponse>(unitOfWork)
    where TRequest : IRequest<TResponse>;
