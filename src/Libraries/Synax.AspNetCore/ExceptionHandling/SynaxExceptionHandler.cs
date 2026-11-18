using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Synax.AspNetCore.ExceptionHandling;

/// <summary>
/// Global exception handler that converts unhandled exceptions to RFC 9457 Problem Details responses.
/// <para>
/// Built-in handling for <see cref="OperationCanceledException"/> (499).
/// </para>
/// <para>
/// Register custom exception mappings via <see cref="SynaxExceptionHandlerOptions.MapException{TException}"/>
/// to extend the handler with application-specific error types.
/// </para>
/// </summary>
public sealed class SynaxExceptionHandler : IExceptionHandler
{
    private readonly ILogger<SynaxExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;
    private readonly SynaxExceptionHandlerOptions _options;

    public SynaxExceptionHandler(
        ILogger<SynaxExceptionHandler> logger,
        IHostEnvironment environment,
        IOptions<SynaxExceptionHandlerOptions> options)
    {
        _logger = logger;
        _environment = environment;
        _options = options.Value;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            _logger.LogError(exception,
                "Unhandled exception after the response had already started; no error response can be written");
            return true;
        }

        IResult problem;

        // Custom mappings take priority; the most-derived registered type wins
        if (ResolveMapping(exception.GetType()) is { } customHandler)
        {
            _logger.LogError(exception, "Handled exception via custom mapping");
            problem = customHandler(httpContext, exception);
        }
        else if (exception is OperationCanceledException canceledException)
        {
            _logger.LogWarning(canceledException, "Operation cancelled");

            problem = Microsoft.AspNetCore.Http.Results.Problem(
                title: "OperationCancelled",
                detail: "An operation was cancelled.",
                statusCode: StatusCodes.Status499ClientClosedRequest);
        }
        else
        {
            _logger.LogError(exception, "Unhandled exception occurred");

            var detail = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred.";

            problem = Microsoft.AspNetCore.Http.Results.Problem(
                title: "Unhandled exception",
                detail: detail,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        await problem.ExecuteAsync(httpContext);

        return true;
    }

    private Func<HttpContext, Exception, IResult>? ResolveMapping(Type exceptionType)
    {
        for (var type = exceptionType; type is not null; type = type.BaseType)
        {
            if (_options.ExceptionMappings.TryGetValue(type, out var handler))
                return handler;
        }

        return null;
    }
}
