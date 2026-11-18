using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.EntityFrameworkCore.Mediator.UnitTests.TestFixtures;

public sealed class TestDbContext : DbContext, ISynaxContext;

/// <summary>
/// Concrete 2-arity subclass of <see cref="TransactionBehavior{TDbContext,TRequest,TResponse}"/>
/// so it can be attached per handler via <c>[MediatorBehavior(typeof(TestTransactionBehavior&lt;,&gt;))]</c>.
/// </summary>
public sealed class TestTransactionBehavior<TRequest, TResponse>(IUnitOfWork<TestDbContext> unitOfWork)
    : TransactionBehavior<TestDbContext, TRequest, TResponse>(unitOfWork)
    where TRequest : IRequest<TResponse>;

public record TestError(string Message);

// One handler per request type — each test outcome gets its own command.
public record SucceedingCommand(string Value) : ICommand<Result<string, TestError>>;

[MediatorBehavior(typeof(TestTransactionBehavior<,>))]
public class SucceedingCommandHandler : ICommandHandler<SucceedingCommand, Result<string, TestError>>
{
    public Task<Result<string, TestError>> HandleAsync(SucceedingCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<string, TestError>>(command.Value);
}

public record FailingCommand : ICommand<Result<string, TestError>>;

[MediatorBehavior(typeof(TestTransactionBehavior<,>))]
public class FailingCommandHandler : ICommandHandler<FailingCommand, Result<string, TestError>>
{
    public Task<Result<string, TestError>> HandleAsync(FailingCommand command, CancellationToken cancellationToken)
        => Task.FromResult(Result.Fail<string, TestError>(new TestError("Business rule violated")));
}

public record ThrowingCommand : ICommand<Result<string, TestError>>;

[MediatorBehavior(typeof(TestTransactionBehavior<,>))]
public class ThrowingCommandHandler : ICommandHandler<ThrowingCommand, Result<string, TestError>>
{
    public Task<Result<string, TestError>> HandleAsync(ThrowingCommand command, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Handler exploded");
}

public record EchoCommand(string Value) : ICommand<string>;

[MediatorBehavior(typeof(TestTransactionBehavior<,>))]
public class EchoCommandHandler : ICommandHandler<EchoCommand, string>
{
    public Task<string> HandleAsync(EchoCommand command, CancellationToken cancellationToken)
        => Task.FromResult(command.Value);
}
