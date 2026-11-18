namespace Synax.Mediator.UnitTests.TestFixtures;

public record ConflictedCommand : ICommand<Guid>;

// These handlers are generic type definitions so assembly scanning skips them
// (otherwise every test scanning this assembly would hit the duplicate-handler
// startup error). The duplicate-registration test closes them (e.g.
// DuplicateHandlerA<object>) and registers them through the registrar directly.
public class DuplicateHandlerA<TMarker> : ICommandHandler<ConflictedCommand, Guid>
{
    public Task<Guid> HandleAsync(ConflictedCommand command, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
}

public class DuplicateHandlerB<TMarker> : ICommandHandler<ConflictedCommand, Guid>
{
    public Task<Guid> HandleAsync(ConflictedCommand command, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
}
