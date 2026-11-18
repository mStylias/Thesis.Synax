namespace Synax.Mediator;

/// <summary>
/// Handler for a void command. Implement <see cref="HandleAsync"/> without returning a value.
/// </summary>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Unit>
    where TCommand : ICommand
{
    /// <summary>
    /// Handles the command.
    /// </summary>
    new Task HandleAsync(TCommand command, CancellationToken cancellationToken);

    async Task<Unit> IRequestHandler<TCommand, Unit>.HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        await HandleAsync(command, cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Handler for a command that returns <typeparamref name="TResponse"/>.
/// </summary>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
}
