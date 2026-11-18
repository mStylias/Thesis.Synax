namespace Synax.Mediator.UnitTests.TestFixtures;

public class LoggingBehavior<TRequest, TResponse>(HandlerSpy state)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue("LoggingBehavior_Before");
        var response = await next();
        state.ExecutedHandlers.Enqueue("LoggingBehavior_After");
        return response;
    }
}

public class TimingBehavior<TRequest, TResponse>(HandlerSpy state)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue("TimingBehavior_Before");
        var response = await next();
        state.ExecutedHandlers.Enqueue("TimingBehavior_After");
        return response;
    }
}

public class LogAttribute() : MediatorBehaviorAttribute(typeof(LoggingBehavior<,>));

public class TimingAttribute() : MediatorBehaviorAttribute(typeof(TimingBehavior<,>));

// Handler decorated with opt-in attribute behavior
public record AuditedCommand(string Data) : ICommand<string>;

[Log]
public class AuditedCommandHandler(HandlerSpy state) : ICommandHandler<AuditedCommand, string>
{
    public Task<string> HandleAsync(AuditedCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(AuditedCommandHandler));
        return Task.FromResult($"Processed: {command.Data}");
    }
}

// Handler decorated with the behavior attribute directly (no derived attribute needed)
public record TimedCommand(string Data) : ICommand<string>;

[MediatorBehavior(typeof(TimingBehavior<,>))]
public class TimedCommandHandler(HandlerSpy state) : ICommandHandler<TimedCommand, string>
{
    public Task<string> HandleAsync(TimedCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(TimedCommandHandler));
        return Task.FromResult($"Timed: {command.Data}");
    }
}

// Handler that skips a global behavior
public record SkippedCommand(string Data) : ICommand;

[SkipBehavior(typeof(LoggingBehavior<,>))]
public class SkippedCommandHandler(HandlerSpy state) : ICommandHandler<SkippedCommand>
{
    public Task HandleAsync(SkippedCommand command, CancellationToken cancellationToken)
    {
        state.ExecutedHandlers.Enqueue(nameof(SkippedCommandHandler));
        return Task.CompletedTask;
    }
}
