using Microsoft.Extensions.Logging;
using Synax.Results;
using Synax.Results.Logging;

namespace Synax.Mediator;

/// <summary>
/// Pipeline behavior that logs failed and warning <see cref="Result"/> responses via
/// <see cref="ResultLoggerExtensions.LogResult"/> — messages, origin, and attached exception
/// land in the log with severity derived from the result status (Error / Warning).
/// Successful results are not logged. Register via <see cref="MediatorOptions.AddResultLogging"/>.
/// </summary>
/// <remarks>
/// The log category is the request type's full name, so log filtering rules can target
/// individual requests or whole feature namespaces.
/// </remarks>
public sealed class ResultLoggingBehavior<TRequest, TResponse>(
    ILoggerFactory loggerFactory, ResultLogOptions? logOptions = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        if (response is Result { Status: not ResultStatus.Success } result)
        {
            var logger = loggerFactory.CreateLogger(typeof(TRequest).FullName ?? typeof(TRequest).Name);
            logger.LogResult(result, logOptions);
        }

        return response;
    }
}
