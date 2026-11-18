namespace Synax.Mediator.Internal;

/// <summary>
/// A discovered handler and everything about it that does not depend on the pipeline:
/// the request/response pair it serves and its pre-built validation error converter.
/// Recorded at discovery time; turned into a <see cref="RequestPipeline{TRequest,TResponse}"/>
/// by <see cref="HandlerRegistrar.BuildPipelines"/> once every module's global behaviors are known.
/// </summary>
internal sealed record HandlerRegistration(
    Type RequestType,
    Type ResponseType,
    Type HandlerType,
    Delegate? ValidationErrorConverter);
