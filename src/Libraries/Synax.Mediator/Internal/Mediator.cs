using System.Diagnostics;
using Synax.Results;

namespace Synax.Mediator.Internal;

internal sealed class Mediator(IServiceProvider serviceProvider, HandlerRegistry registry, MediatorOptions mediatorOptions) : IMediator
{
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var pipeline = registry.GetRequestPipeline<TResponse>(requestType);

        return MediatorDiagnostics.SendEnabled
            ? SendInstrumentedAsync(requestType, pipeline, request, cancellationToken)
            : pipeline.ExecuteAsync(request, serviceProvider, cancellationToken);
    }

    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        return PublishCoreAsync(notification, mediatorOptions.NotificationPublishStrategy, cancellationToken);
    }

    public Task PublishAsync<TNotification>(TNotification notification, NotificationPublishStrategy strategy, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        return PublishCoreAsync(notification, strategy, cancellationToken);
    }

    private async Task<TResponse> SendInstrumentedAsync<TResponse>(
        Type requestType,
        RequestPipeline<TResponse> pipeline,
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        var requestName = requestType.Name;
        using var activity = MediatorDiagnostics.ActivitySource.StartActivity($"send {requestName}");
        activity?.SetTag("synax.request_type", requestName);

        var start = Stopwatch.GetTimestamp();
        var status = "ok";

        try
        {
            var response = await pipeline.ExecuteAsync(request, serviceProvider, cancellationToken);

            if (response is Result { IsError: true } failed)
            {
                status = "error";
                activity?.SetStatus(ActivityStatusCode.Error, "Handler returned a failed result.");
                EnrichWithFailedResult(activity, failed);
            }
            else if (response is Result { IsWarning: true })
            {
                activity?.SetTag("synax.result_status", "warning");
            }

            return response;
        }
        catch (Exception ex)
        {
            status = "exception";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
        finally
        {
            MediatorDiagnostics.RecordSend(requestName, status, Stopwatch.GetElapsedTime(start));
        }
    }

    private static void EnrichWithFailedResult(Activity? activity, Result failed)
    {
        if (activity is null) return;

        if (failed.ErrorObject is IHasErrorCode { ErrorCode: { Length: > 0 } errorCode })
            activity.SetTag("synax.error_code", errorCode);

        if (failed.Origin is { } origin)
            activity.SetTag("synax.result_origin", origin.ToString());

        if (failed.Messages.Count > 0)
            activity.SetTag("synax.result_messages", string.Join("; ", failed.Messages));

        if (failed.Exception is { } exception)
            activity.AddException(exception);
    }

    private Task PublishCoreAsync<TNotification>(TNotification notification, NotificationPublishStrategy strategy, CancellationToken cancellationToken)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var notificationType = notification.GetType();

        if (!registry.HasNotificationPipeline(notificationType))
            return Task.CompletedTask;

        var pipeline = registry.GetNotificationPipeline(notificationType);

        return MediatorDiagnostics.PublishEnabled
            ? PublishInstrumentedAsync(notificationType, pipeline, notification, strategy, cancellationToken)
            : pipeline.ExecuteAsync(notification, serviceProvider, strategy, cancellationToken);
    }

    private async Task PublishInstrumentedAsync(
        Type notificationType,
        NotificationPipeline pipeline,
        INotification notification,
        NotificationPublishStrategy strategy,
        CancellationToken cancellationToken)
    {
        var notificationName = notificationType.Name;
        using var activity = MediatorDiagnostics.ActivitySource.StartActivity($"publish {notificationName}");
        activity?.SetTag("synax.notification_type", notificationName);
        activity?.SetTag("synax.publish_strategy", strategy.ToString());

        var start = Stopwatch.GetTimestamp();
        var status = "ok";

        try
        {
            await pipeline.ExecuteAsync(notification, serviceProvider, strategy, cancellationToken);
        }
        catch (Exception ex)
        {
            status = "exception";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
        finally
        {
            MediatorDiagnostics.RecordPublish(notificationName, status, Stopwatch.GetElapsedTime(start));
        }
    }
}
