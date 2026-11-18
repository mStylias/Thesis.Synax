using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Synax.Mediator.Internal;

/// <summary>
/// Telemetry for the mediator: one span per send/publish plus count/duration metrics.
/// Everything is keyed by <see cref="MediatorTelemetry.SourceName"/>; when nothing listens,
/// callers skip instrumentation entirely via <see cref="SendEnabled"/>/<see cref="PublishEnabled"/>.
/// </summary>
internal static class MediatorDiagnostics
{
    internal static readonly ActivitySource ActivitySource = new(MediatorTelemetry.SourceName);
    private static readonly Meter Meter = new(MediatorTelemetry.SourceName);

    private static readonly Counter<long> RequestCount = Meter.CreateCounter<long>(
        "synax.mediator.requests",
        description: "Number of requests sent through the mediator.");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "synax.mediator.request.duration",
        unit: "s",
        description: "Duration of request handling, including pipeline behaviors.");

    private static readonly Counter<long> NotificationCount = Meter.CreateCounter<long>(
        "synax.mediator.notifications",
        description: "Number of notifications published through the mediator.");

    private static readonly Histogram<double> NotificationDuration = Meter.CreateHistogram<double>(
        "synax.mediator.notification.duration",
        unit: "s",
        description: "Duration of notification publishing across all handlers.");

    internal static bool SendEnabled
        => ActivitySource.HasListeners() || RequestCount.Enabled || RequestDuration.Enabled;

    internal static bool PublishEnabled
        => ActivitySource.HasListeners() || NotificationCount.Enabled || NotificationDuration.Enabled;

    internal static void RecordSend(string requestType, string status, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "synax.request_type", requestType },
            { "synax.status", status }
        };

        RequestCount.Add(1, tags);
        RequestDuration.Record(elapsed.TotalSeconds, tags);
    }

    internal static void RecordPublish(string notificationType, string status, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "synax.notification_type", notificationType },
            { "synax.status", status }
        };

        NotificationCount.Add(1, tags);
        NotificationDuration.Record(elapsed.TotalSeconds, tags);
    }
}
