namespace Synax.Mediator.UnitTests.TestFixtures;

// Fixtures reserved for TelemetryTests: their type names appear in activity operation
// names and metric tags, so keeping them out of other test classes lets the telemetry
// assertions filter precisely even if unrelated tests emit telemetry concurrently.
public record TelemetryProbeCommand(string Data) : ICommand<string>;

public class TelemetryProbeCommandHandler : ICommandHandler<TelemetryProbeCommand, string>
{
    public Task<string> HandleAsync(TelemetryProbeCommand command, CancellationToken cancellationToken)
        => Task.FromResult(command.Data);
}

public record TelemetryProbeNotification(Guid Id) : INotification;

public class TelemetryProbeNotificationHandler : INotificationHandler<TelemetryProbeNotification>
{
    public Task HandleAsync(TelemetryProbeNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
