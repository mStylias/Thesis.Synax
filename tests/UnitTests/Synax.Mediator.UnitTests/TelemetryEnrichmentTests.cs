using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.UnitTests.TestFixtures;

namespace Synax.Mediator.UnitTests;

/// <summary>
/// Tests for failed/warning-result span enrichment (<c>synax.error_code</c>,
/// <c>synax.result_origin</c>, <c>synax.result_messages</c>, <c>synax.result_status</c>,
/// exception events).
/// <para>
/// <c>ActivityListener</c> is process-global, so these tests run non-parallel, filter
/// listeners on the exact source name, and scope every assertion to activities referencing
/// this class's own fixture types (see <see cref="TelemetryTests"/>).
/// </para>
/// </summary>
[NotInParallel]
public class TelemetryEnrichmentTests
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
    public async Task SendAsync_WhenHandlerReturnsFailedResult_ShouldTagErrorCodeOriginAndMessages()
    {
        // Arrange
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = StartActivityListener(stopped);
        var mediator = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new EnrichedFailCommand());

        // Assert
        await Assert.That(result.IsError).IsTrue();
        var activities = stopped
            .Where(a => a.OperationName == $"send {nameof(EnrichedFailCommand)}")
            .ToArray();
        await Assert.That(activities).Count().IsEqualTo(1);
        await Assert.That(activities[0].Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That((string?)activities[0].GetTagItem("synax.error_code"))
            .IsEqualTo("ENRICHED_FAIL");
        // Origin is captured where Result.Fail was called — the handler's HandleAsync.
        await Assert.That((string?)activities[0].GetTagItem("synax.result_origin"))
            .Contains("HandleAsync");
        await Assert.That((string?)activities[0].GetTagItem("synax.result_messages"))
            .IsEqualTo("first message");
    }

    [Test]
    public async Task SendAsync_WhenHandlerReturnsWarningResult_ShouldTagResultStatusWarning()
    {
        // Arrange
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = StartActivityListener(stopped);
        var mediator = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new EnrichedWarnCommand());

        // Assert
        await Assert.That(result.IsWarning).IsTrue();
        var activities = stopped
            .Where(a => a.OperationName == $"send {nameof(EnrichedWarnCommand)}")
            .ToArray();
        await Assert.That(activities).Count().IsEqualTo(1);
        await Assert.That(activities[0].Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That((string?)activities[0].GetTagItem("synax.result_status"))
            .IsEqualTo("warning");
    }

    [Test]
    public async Task SendAsync_WhenFailedResultCarriesException_ShouldAddExceptionEvent()
    {
        // Arrange
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = StartActivityListener(stopped);
        var mediator = CreateMediator();

        // Act
        var result = await mediator.SendAsync(new EnrichedExceptionCommand());

        // Assert
        await Assert.That(result.IsError).IsTrue();
        var activities = stopped
            .Where(a => a.OperationName == $"send {nameof(EnrichedExceptionCommand)}")
            .ToArray();
        await Assert.That(activities).Count().IsEqualTo(1);
        var exceptionEvents = activities[0].Events
            .Where(e => e.Name == "exception")
            .ToArray();
        await Assert.That(exceptionEvents).Count().IsEqualTo(1);
    }
}
