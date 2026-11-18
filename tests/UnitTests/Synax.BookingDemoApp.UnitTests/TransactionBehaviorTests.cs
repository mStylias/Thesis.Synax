using Synax.BookingDemoApp.Domain;
using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Application.Behaviors;
using Synax.BookingDemoApp.UnitTests.TestSupport;
using Synax.EntityFrameworkCore.Testing;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: the fake's tracked no-op transactions asserting the library's
/// <c>TransactionBehavior</c> contract through the demo's <see cref="BookingTransactionBehavior{TRequest,TResponse}"/> —
/// commit on a successful <c>Result</c>, rollback on a failed one, rollback + rethrow on exception.
/// </summary>
public class TransactionBehaviorTests
{
    /// <summary>A minimal request type just to close the behavior's generic parameters.</summary>
    private sealed class PingRequest : ICommand<Result<string, HttpError>>;

    private static BookingTransactionBehavior<PingRequest, Result<string, HttpError>> CreateBehavior(
        out FakeUnitOfWork<BookingContext> uow)
    {
        uow = TestWorld.CreateUow();
        return new BookingTransactionBehavior<PingRequest, Result<string, HttpError>>(uow);
    }

    [Test]
    public async Task SuccessfulResult_CommitsTheTransaction()
    {
        var behavior = CreateBehavior(out var uow);

        var response = await behavior.HandleAsync(
            new PingRequest(),
            () => Task.FromResult<Result<string, HttpError>>(Result.Ok<string, HttpError>("done")),
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(uow.Transactions).Count().IsEqualTo(1);
        await Assert.That(uow.Transactions[0].WasCommitted).IsTrue();
        await Assert.That(uow.Transactions[0].WasRolledBack).IsFalse();
    }

    [Test]
    public async Task FailedResult_RollsBackTheTransaction()
    {
        var behavior = CreateBehavior(out var uow);

        var response = await behavior.HandleAsync(
            new PingRequest(),
            () => Task.FromResult<Result<string, HttpError>>(
                Result.Fail<string, HttpError>(HttpError.Conflict("nope"))),
            CancellationToken.None);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(uow.Transactions[0].WasRolledBack).IsTrue();
        await Assert.That(uow.Transactions[0].WasCommitted).IsFalse();
    }

    [Test]
    public async Task ThrowingHandler_RollsBack_AndRethrows()
    {
        var behavior = CreateBehavior(out var uow);
        RequestHandlerDelegate<Result<string, HttpError>> next =
            () => throw new InvalidOperationException("boom");

        await Assert.That(() => behavior.HandleAsync(new PingRequest(), next, CancellationToken.None))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("boom");

        await Assert.That(uow.Transactions[0].WasRolledBack).IsTrue();
        await Assert.That(uow.TransactionRollbacks).IsEqualTo(1);
        await Assert.That(uow.TransactionCommits).IsEqualTo(0);
    }
}
