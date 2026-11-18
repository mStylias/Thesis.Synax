using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

/// <summary>
/// Tests for the mediator's built-in telemetry (spans via <c>ActivitySource</c> and metrics
/// via <c>Meter</c>, both named <see cref="MediatorTelemetry.SourceName"/>).
/// <para>
/// <c>ActivityListener</c>/<c>MeterListener</c> are process-global, so these tests run
/// non-parallel, filter listeners on the exact source name, and scope every assertion to
/// activities/measurements referencing this class's own fixture types.
/// </para>
/// </summary>
[NotInParallel]
public class TelemetryTests
{
    private static IMediator CreateMediator()
    {
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSynaxMediator(opts => opts.AddHandlersFromAssemblyContaining<HandlerSpy>());

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    private static ActivityListener StartActivityListener(ConcurrentQueue<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MediatorTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Test]
    public async Task SendAsync_WhenActivityListenerAttached_ShouldCreateSendActivityWithRequestTypeTag()
    {
        // Arrange
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = StartActivityListener(stopped);
        var mediator = CreateMediator();

        // Act
        await mediator.SendAsync(new TelemetryProbeCommand("ping"));

        // Assert
        var activities = stopped
            .Where(a => a.OperationName == $"send {nameof(TelemetryProbeCommand)}")
            .ToArray();
        await Assert.That(activities).Count().IsEqualTo(1);
        await Assert.That(activities[0].Source.Name).IsEqualTo(MediatorTelemetry.SourceName);
        await Assert.That((string?)activities[0].GetTagItem("synax.request_type"))
            .IsEqualTo(nameof(TelemetryProbeCommand));
        await Assert.That(activities[0].Status).IsEqualTo(ActivityStatusCode.Unset);
    }

    [Test]
    public async Task SendAsync_WhenHandlerReturnsFailedResult_ShouldSetActivityStatusError()
    {
        // Arrange
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = StartActivityListener(stopped);
        var mediator = CreateMediator();

        // Act — negative amount fails ProcessOrderHandler's validation, which short-circuits
        // the pipeline with a failed Result
        var result = await mediator.SendAsync(new ProcessOrderCommand("ORD-1", -5m));

        // Assert
        await Assert.That(result.IsError).IsTrue();
        var activities = stopped
            .Where(a => a.OperationName == $"send {nameof(ProcessOrderCommand)}")
            .ToArray();
        await Assert.That(activities).Count().IsEqualTo(1);
        await Assert.That(activities[0].Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task PublishAsync_WhenActivityListenerAttached_ShouldCreatePublishActivityWithNotificationTypeTag()
    {
        // Arrange
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = StartActivityListener(stopped);
        var mediator = CreateMediator();

        // Act
        await mediator.PublishAsync(new TelemetryProbeNotification(Guid.NewGuid()));

        // Assert
        var activities = stopped
            .Where(a => a.OperationName == $"publish {nameof(TelemetryProbeNotification)}")
            .ToArray();
        await Assert.That(activities).Count().IsEqualTo(1);
        await Assert.That((string?)activities[0].GetTagItem("synax.notification_type"))
            .IsEqualTo(nameof(TelemetryProbeNotification));
        await Assert.That(activities[0].Status).IsEqualTo(ActivityStatusCode.Unset);
    }

    [Test]
    public async Task SendAsync_WhenMeterListenerAttached_ShouldRecordRequestCounterWithOkStatus()
    {
        // Arrange
        var measurements = new ConcurrentQueue<(long Value, string? RequestType, string? Status)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MediatorTelemetry.SourceName
                    && instrument.Name == "synax.mediator.requests")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? requestType = null;
            string? status = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "synax.request_type") requestType = tag.Value as string;
                if (tag.Key == "synax.status") status = tag.Value as string;
            }

            measurements.Enqueue((value, requestType, status));
        });
        meterListener.Start();
        var mediator = CreateMediator();

        // Act
        await mediator.SendAsync(new TelemetryProbeCommand("ping"));

        // Assert
        var recorded = measurements
            .Where(m => m.RequestType == nameof(TelemetryProbeCommand))
            .ToArray();
        await Assert.That(recorded).Count().IsEqualTo(1);
        await Assert.That(recorded[0].Value).IsEqualTo(1L);
        await Assert.That(recorded[0].Status).IsEqualTo("ok");
    }
}
