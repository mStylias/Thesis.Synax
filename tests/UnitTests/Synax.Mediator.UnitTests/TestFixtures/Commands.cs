namespace Synax.Mediator.UnitTests.TestFixtures;

public record CreateBookingCommand(string Name, decimal Price) : ICommand<Guid>;

public class CreateBookingCommandHandler(HandlerSpy state) : ICommandHandler<CreateBookingCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateBookingCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(CreateBookingCommandHandler));
        state.LastReceivedToken = cancellationToken;
        return Task.FromResult(Guid.NewGuid());
    }
}

public record DeleteBookingCommand(Guid Id) : ICommand;

public class DeleteBookingCommandHandler(HandlerSpy state) : ICommandHandler<DeleteBookingCommand>
{
    public Task HandleAsync(DeleteBookingCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(DeleteBookingCommandHandler));
        return Task.CompletedTask;
    }
}

public record UnregisteredCommand : ICommand;
