using Microsoft.Extensions.DependencyInjection;
using Synax.Results;

namespace Synax.Mediator.Internal;

/// <summary>
/// Type-erased base so the registry can store pipelines with different TResponse in one collection.
/// </summary>
internal abstract class RequestPipeline<TResponse>
{
    public abstract Task<TResponse> ExecuteAsync(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>
/// Executes the full pipeline for a request: behaviors (if any) → validation → handler.
/// <paramref name="behaviorTypes"/> is a pre-built array from startup; at runtime the chain is built
/// from innermost (handler) outward by iterating in reverse.
/// </summary>
internal sealed class RequestPipeline<TRequest, TResponse>(
    Type[] behaviorTypes,
    Func<Result, TResponse>? validationErrorConverter) : RequestPipeline<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override async Task<TResponse> ExecuteAsync(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var typedRequest = (TRequest)request;

        if (behaviorTypes.Length == 0)
            return await ExecuteHandler(serviceProvider, typedRequest, cancellationToken);

        RequestHandlerDelegate<TResponse> next = () =>
            ExecuteHandler(serviceProvider, typedRequest, cancellationToken);

        for (var i = behaviorTypes.Length - 1; i >= 0; i--)
        {
            var behavior = (IPipelineBehavior<TRequest, TResponse>)serviceProvider.GetRequiredService(behaviorTypes[i]);
            var previousNext = next;
            next = () => behavior.HandleAsync(typedRequest, previousNext, cancellationToken);
        }

        return await next();
    }

    private async Task<TResponse> ExecuteHandler(
        IServiceProvider serviceProvider,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        if (validationErrorConverter is not null && handler is IValidatable<TRequest> validatable)
        {
            var result = await validatable.ValidateAsResultAsync(request, cancellationToken);

            if (result.IsError)
                return validationErrorConverter(result);
        }

        return await handler.HandleAsync(request, cancellationToken);
    }
}
