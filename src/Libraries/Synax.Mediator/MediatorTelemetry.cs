namespace Synax.Mediator;

/// <summary>
/// Names of the telemetry sources emitted by the mediator. Subscribe an OpenTelemetry
/// tracer/meter provider (or any <c>ActivityListener</c>/<c>MeterListener</c>) to these
/// names to receive spans and metrics; with no listener attached the overhead is negligible.
/// </summary>
public static class MediatorTelemetry
{
    /// <summary>
    /// Name of both the <c>ActivitySource</c> (spans per send/publish) and the <c>Meter</c>
    /// (request/notification counters and duration histograms).
    /// </summary>
    public const string SourceName = "Synax.Mediator";
}
