using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Synax.Mediator;

namespace Synax.BookingDemoApp.Application.Behaviors;

/// <summary>
/// Times every request it is attached to. Registered globally for all commands (see
/// <c>AddApplication</c>'s <c>appliesTo</c> predicate); individual query handlers can opt in with
/// <c>[Timed]</c> and command handlers can opt out with <c>[SkipBehavior]</c>.
/// </summary>
public class RequestTimingBehavior<TRequest, TResponse>(ILogger<RequestTimingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var response = await next();
        var elapsed = Stopwatch.GetElapsedTime(start);

        logger.LogInformation(
            "{RequestType} handled in {ElapsedMs:F1} ms", typeof(TRequest).Name, elapsed.TotalMilliseconds);

        return response;
    }
}

/// <summary>
/// Named attribute for <see cref="RequestTimingBehavior{TRequest,TResponse}"/> — subclassing
/// <see cref="MediatorBehaviorAttribute"/> gives the attachment a domain name.
/// </summary>
public class TimedAttribute() : MediatorBehaviorAttribute(typeof(RequestTimingBehavior<,>));
