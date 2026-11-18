using Synax.Results;

namespace Synax.Mediator.UnitTests.TestFixtures;

public record OrderData(string OrderId);

public record OrderError(string Message);

public record ProcessOrderCommand(string OrderId, decimal Amount) : ICommand<Result<OrderData, OrderError>>;

public class ProcessOrderHandler(HandlerSpy state)
    : ICommandHandler<ProcessOrderCommand, Result<OrderData, OrderError>>,
      IWithValidation<ProcessOrderCommand, Result<OrderError>>
{
    public Task<Result<OrderError>> ValidateAsync(
        ProcessOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
            return Task.FromResult<Result<OrderError>>(
                Result.Fail(new OrderError("Amount must be positive")));

        return Task.FromResult<Result<OrderError>>(Result.Ok());
    }

    public Task<Result<OrderData, OrderError>> HandleAsync(
        ProcessOrderCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(ProcessOrderHandler));
        return Task.FromResult<Result<OrderData, OrderError>>(
            Result.Ok(new OrderData(command.OrderId)));
    }
}

public record ShipOrderCommand(string OrderId) : ICommand<Result<OrderData, OrderError>>;

/// <summary>
/// Data-carrying validation: ValidateAsync returns the validated state
/// (<c>Result&lt;string, OrderError&gt;</c>), which HandleAsync reads via <c>this.ValidationData()</c>.
/// </summary>
public class ShipOrderHandler(HandlerSpy state)
    : ICommandHandler<ShipOrderCommand, Result<OrderData, OrderError>>,
      IWithValidation<ShipOrderCommand, Result<string, OrderError>>
{
    public Task<Result<string, OrderError>> ValidateAsync(
        ShipOrderCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId))
            return Task.FromResult<Result<string, OrderError>>(
                Result.Fail(new OrderError("Order id is required")));

        return Task.FromResult<Result<string, OrderError>>($"validated:{command.OrderId}");
    }

    public Task<Result<OrderData, OrderError>> HandleAsync(
        ShipOrderCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(ShipOrderHandler));
        var validated = this.ValidationData();
        return Task.FromResult<Result<OrderData, OrderError>>(Result.Ok(new OrderData(validated)));
    }
}

public record MismatchedValidationCommand : ICommand<Result<OrderData, OrderError>>;

// Generic type definition so assembly scanning skips it (registering it must throw);
// the mismatch test closes it and registers through the registrar directly.
public class MismatchedValidationHandler<TMarker>
    : ICommandHandler<MismatchedValidationCommand, Result<OrderData, OrderError>>,
      IWithValidation<MismatchedValidationCommand, Result<string, string>>
{
    public Task<Result<string, string>> ValidateAsync(
        MismatchedValidationCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<string, string>>(Result.Ok<string, string>("ok"));

    public Task<Result<OrderData, OrderError>> HandleAsync(
        MismatchedValidationCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<OrderData, OrderError>>(Result.Ok(new OrderData("x")));
}

public abstract record OrderCommandBase(decimal Amount);

public record RefundOrderCommand(string OrderId, decimal Amount)
    : OrderCommandBase(Amount), ICommand<Result<OrderData, OrderError>>;

/// <summary>
/// Declares validation against the request's base type — exercises the contravariant
/// <c>IValidatable&lt;in TRequest&gt;</c> path in the registrar.
/// </summary>
public class RefundOrderHandler(HandlerSpy state)
    : ICommandHandler<RefundOrderCommand, Result<OrderData, OrderError>>,
      IWithValidation<OrderCommandBase, Result<OrderError>>
{
    public Task<Result<OrderError>> ValidateAsync(
        OrderCommandBase command, CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
            return Task.FromResult<Result<OrderError>>(
                Result.Fail(new OrderError("Refund amount must be positive")));

        return Task.FromResult<Result<OrderError>>(Result.Ok());
    }

    public Task<Result<OrderData, OrderError>> HandleAsync(
        RefundOrderCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(RefundOrderHandler));
        return Task.FromResult<Result<OrderData, OrderError>>(
            Result.Ok(new OrderData(command.OrderId)));
    }
}
