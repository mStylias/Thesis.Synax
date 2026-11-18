using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator.UnitTests.TestFixtures;
using Synax.EntityFrameworkCore.Testing;
using Synax.Mediator;

namespace Synax.EntityFrameworkCore.Mediator.UnitTests;

public class TransactionBehaviorTests
{
    private static (IMediator Mediator, FakeUnitOfWork<TestDbContext> UnitOfWork) CreateMediator()
    {
        var unitOfWork = new FakeUnitOfWork<TestDbContext>();

        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork<TestDbContext>>(unitOfWork);
        services.AddSynaxMediator(opts => opts.AddHandlersFromAssemblyContaining<TestDbContext>());

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(), unitOfWork);
    }

    [Test]
    public async Task SendAsync_WhenHandlerReturnsSuccess_ShouldCommitTransaction()
    {
        // Arrange
        var (mediator, unitOfWork) = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new SucceedingCommand("payload"));

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo("payload");
        await Assert.That(unitOfWork.Transactions).Count().IsEqualTo(1);
        await Assert.That(unitOfWork.Transactions[0].WasCommitted).IsTrue();
        await Assert.That(unitOfWork.Transactions[0].WasRolledBack).IsFalse();
    }

    [Test]
    public async Task SendAsync_WhenHandlerReturnsFailedResult_ShouldRollbackTransaction()
    {
        // Arrange
        var (mediator, unitOfWork) = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new FailingCommand());

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(unitOfWork.Transactions).Count().IsEqualTo(1);
        await Assert.That(unitOfWork.Transactions[0].WasRolledBack).IsTrue();
        await Assert.That(unitOfWork.Transactions[0].WasCommitted).IsFalse();
    }

    [Test]
    public async Task SendAsync_WhenHandlerThrows_ShouldRollbackTransactionAndRethrow()
    {
        // Arrange
        var (mediator, unitOfWork) = CreateMediator();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.SendAsync(new ThrowingCommand()));

        // Assert — exception propagated to the caller and the transaction was rolled back
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).IsEqualTo("Handler exploded");
        await Assert.That(unitOfWork.Transactions).Count().IsEqualTo(1);
        await Assert.That(unitOfWork.Transactions[0].WasRolledBack).IsTrue();
        await Assert.That(unitOfWork.Transactions[0].WasCommitted).IsFalse();
    }

    [Test]
    public async Task SendAsync_WhenResponseIsNotAResult_ShouldCommitTransaction()
    {
        // Arrange
        var (mediator, unitOfWork) = CreateMediator();

        // Act
        var response = await mediator.SendAsync(new EchoCommand("hello"));

        // Assert — a plain (non-Result) response always commits
        await Assert.That(response).IsEqualTo("hello");
        await Assert.That(unitOfWork.Transactions).Count().IsEqualTo(1);
        await Assert.That(unitOfWork.Transactions[0].WasCommitted).IsTrue();
        await Assert.That(unitOfWork.Transactions[0].WasRolledBack).IsFalse();
    }
}
