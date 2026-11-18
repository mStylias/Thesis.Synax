using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Synax.Results;

namespace Synax.Mediator.Internal;

/// <summary>
/// Scans assemblies at startup to register handlers, build pipeline behavior chains,
/// and populate the <see cref="HandlerRegistry"/>. No reflection occurs at runtime.
/// <para>
/// Discovery (<see cref="RegisterHandlerType"/>) and pipeline construction
/// (<see cref="BuildPipelines"/>) are separate steps: the pipeline of a handler depends on the
/// global behaviors of <b>every</b> registration call, which are not all known while an
/// individual call is still scanning.
/// </para>
/// </summary>
internal sealed class HandlerRegistrar(
    IServiceCollection services,
    ServiceLifetime lifetime,
    HandlerRegistry registry)
{
    private static readonly Type RequestHandlerOpenType = typeof(IRequestHandler<,>);
    private static readonly Type CommandHandlerVoidOpenType = typeof(ICommandHandler<>);
    private static readonly Type CommandHandlerOpenType = typeof(ICommandHandler<,>);
    private static readonly Type QueryHandlerOpenType = typeof(IQueryHandler<,>);
    private static readonly Type NotificationHandlerOpenType = typeof(INotificationHandler<>);
    private static readonly Type WithValidationOpenType = typeof(IWithValidation<,>);
    private static readonly Type PipelineBehaviorOpenType = typeof(IPipelineBehavior<,>);
    private static readonly Type ResultWithErrorOpenType = typeof(Result<>);
    private static readonly Type ResultWithDataAndErrorOpenType = typeof(Result<,>);

    /// <summary>
    /// Whether <paramref name="type"/> implements any handler interface — the same test
    /// <see cref="RegisterHandlerType"/> applies, shared so module surface validation and
    /// registration cannot disagree about what counts as a handler.
    /// </summary>
    internal static bool IsHandlerType(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (!@interface.IsGenericType)
                continue;

            var genericDef = @interface.GetGenericTypeDefinition();

            if (genericDef == RequestHandlerOpenType
                || genericDef == CommandHandlerVoidOpenType
                || genericDef == CommandHandlerOpenType
                || genericDef == QueryHandlerOpenType
                || genericDef == NotificationHandlerOpenType)
            {
                return true;
            }
        }

        return false;
    }

    internal void RegisterHandlers(Assembly assembly)
    {
        var concreteTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var type in concreteTypes)
            RegisterHandlerType(type);
    }

    internal void RegisterHandlerType(Type type, bool skipIfRequestHandled = false)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (!@interface.IsGenericType)
                continue;

            var genericDef = @interface.GetGenericTypeDefinition();

            if (genericDef == NotificationHandlerOpenType)
            {
                RegisterNotificationHandler(type, @interface);
                continue;
            }

            Type requestType, responseType;

            if (genericDef == CommandHandlerVoidOpenType)
            {
                requestType = @interface.GetGenericArguments()[0];
                responseType = typeof(Unit);
            }
            else if (genericDef == CommandHandlerOpenType
                     || genericDef == QueryHandlerOpenType
                     || genericDef == RequestHandlerOpenType)
            {
                var args = @interface.GetGenericArguments();
                requestType = args[0];
                responseType = args[1];
            }
            else
            {
                continue;
            }

            // Explicit registrations yield to an existing handler for the same request —
            // this is what lets a handwritten handler override an added default.
            if (skipIfRequestHandled
                && registry.TryGetRequestHandlerType(requestType, out var alreadyRegistered)
                && alreadyRegistered != type)
            {
                continue;
            }

            AddHandlerDescriptor(@interface, type);

            // The pipeline resolves handlers via IRequestHandler<,>, so the marker
            // interfaces (ICommandHandler/IQueryHandler) also register under it.
            if (genericDef != RequestHandlerOpenType)
                AddHandlerDescriptor(RequestHandlerOpenType.MakeGenericType(requestType, responseType), type);

            RecordHandler(type, requestType, responseType);
        }
    }

    /// <summary>
    /// (Re)builds the pipeline of every recorded handler from the registry's accumulated global
    /// behaviors. Called at the end of every <c>AddSynaxMediator</c> call, so a behavior added by
    /// a later call also reaches handlers discovered by an earlier one — module registration
    /// order does not change the resulting pipelines.
    /// </summary>
    internal void BuildPipelines()
    {
        foreach (var handler in registry.Handlers)
        {
            var behaviorTypes = BuildPipelineBehaviorTypes(
                handler.HandlerType, handler.RequestType, handler.ResponseType);

            RegisterBehaviorServices(behaviorTypes);

            var pipelineGenericType = typeof(RequestPipeline<,>)
                .MakeGenericType(handler.RequestType, handler.ResponseType);
            var pipeline = Activator.CreateInstance(
                pipelineGenericType, behaviorTypes, handler.ValidationErrorConverter)!;

            registry.SetRequestPipeline(handler.RequestType, pipeline);
        }
    }

    private void RegisterNotificationHandler(Type implementationType, Type notificationHandlerInterface)
    {
        var notificationType = notificationHandlerInterface.GetGenericArguments()[0];

        AddHandlerDescriptor(notificationHandlerInterface, implementationType);

        if (!registry.HasNotificationPipeline(notificationType))
        {
            var pipelineType = typeof(NotificationPipeline<>).MakeGenericType(notificationType);
            var pipeline = (NotificationPipeline)Activator.CreateInstance(pipelineType)!;
            registry.RegisterNotificationPipeline(notificationType, pipeline);
        }
    }

    private void AddHandlerDescriptor(Type serviceType, Type implementationType)
    {
        if (!services.Any(sd => sd.ServiceType == serviceType && sd.ImplementationType == implementationType))
            services.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
    }

    /// <summary>
    /// Records the handler so <see cref="BuildPipelines"/> can wire it up later, enforcing the
    /// one-handler-per-request rule at the registration that breaks it.
    /// </summary>
    private void RecordHandler(Type handlerType, Type requestType, Type responseType)
    {
        if (registry.TryGetRequestHandlerType(requestType, out var existingHandlerType))
        {
            // The same handler reached through a second handler interface — already recorded.
            if (existingHandlerType == handlerType)
                return;

            throw new InvalidOperationException(
                $"Duplicate handler registration for request '{requestType.Name}': " +
                $"both '{existingHandlerType.Name}' and '{handlerType.Name}' handle it. " +
                "A request must have exactly one handler.");
        }

        // Built here rather than in BuildPipelines: the converter depends only on the handler,
        // so a mismatched validation/response pair fails at the registration that introduced it
        // instead of on an unrelated module's merge call.
        var errorConverter = BuildValidationErrorConverter(handlerType, requestType, responseType);

        registry.AddHandler(new HandlerRegistration(requestType, responseType, handlerType, errorConverter));
    }

    private void RegisterBehaviorServices(Type[] behaviorTypes)
    {
        foreach (var behaviorType in behaviorTypes)
        {
            // Pipelines are rebuilt on every registration call; the registry remembers which
            // behaviors already have a descriptor so only the first build scans the collection.
            if (!registry.TryMarkBehaviorRegistered(behaviorType))
                continue;

            if (services.All(sd => sd.ServiceType != behaviorType))
                services.Add(new ServiceDescriptor(behaviorType, behaviorType, lifetime));
        }
    }

    /// <summary>
    /// If the handler implements <see cref="IWithValidation{TRequest, TValidation}"/>,
    /// returns a pre-built <c>Func&lt;Result, TResponse&gt;</c> that converts a validation error
    /// into the handler's response type. Returns <c>null</c> if the handler has no validation.
    /// </summary>
    private Delegate? BuildValidationErrorConverter(Type handlerType, Type requestType, Type responseType)
    {
        Type? validationType = null;
        foreach (var iface in handlerType.GetInterfaces())
        {
            if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != WithValidationOpenType)
                continue;

            var args = iface.GetGenericArguments();
            if (args[0] == requestType)
            {
                validationType = args[1];
                break;
            }

            // IValidatable<in TRequest> is contravariant, so validation declared against
            // a base type of the request still applies; an exact match (above) wins.
            if (args[0].IsAssignableFrom(requestType))
                validationType ??= args[1];
        }

        if (validationType is null)
            return null;

        // Result<TError> validation → Result<TData, TError> response
        if (responseType.IsGenericType
            && responseType.GetGenericTypeDefinition() == ResultWithDataAndErrorOpenType
            && validationType.IsGenericType
            && validationType.GetGenericTypeDefinition() == ResultWithErrorOpenType)
        {
            var tData = responseType.GetGenericArguments()[0];
            var tError = responseType.GetGenericArguments()[1];
            var validationTError = validationType.GetGenericArguments()[0];

            if (tError != validationTError)
                throw new InvalidOperationException(
                    $"Handler '{handlerType.Name}' validates with {validationType.Name} " +
                    $"but returns {responseType.Name}. The TError types must match.");

            var method = typeof(HandlerRegistrar)
                .GetMethod(nameof(ConvertToDataError), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(tData, tError);

            return Delegate.CreateDelegate(
                typeof(Func<,>).MakeGenericType(typeof(Result), responseType), method);
        }

        // Result<TError> validation → Result<TError> response (same type)
        if (responseType == validationType)
        {
            var method = typeof(HandlerRegistrar)
                .GetMethod(nameof(DirectCast), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(responseType);

            return Delegate.CreateDelegate(
                typeof(Func<,>).MakeGenericType(typeof(Result), responseType), method);
        }

        // Result<TState, TError> validation (data-carrying — read in the handler via
        // this.ValidationData()) → Result<TData, TError> response
        if (responseType.IsGenericType
            && responseType.GetGenericTypeDefinition() == ResultWithDataAndErrorOpenType
            && validationType.IsGenericType
            && validationType.GetGenericTypeDefinition() == ResultWithDataAndErrorOpenType)
        {
            var tData = responseType.GetGenericArguments()[0];
            var tError = responseType.GetGenericArguments()[1];
            var validationTError = validationType.GetGenericArguments()[1];

            if (tError != validationTError)
                throw new InvalidOperationException(
                    $"Handler '{handlerType.Name}' validates with {validationType.Name} " +
                    $"but returns {responseType.Name}. The TError types must match.");

            var method = typeof(HandlerRegistrar)
                .GetMethod(nameof(ConvertAnyToDataError), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(tData, tError);

            return Delegate.CreateDelegate(
                typeof(Func<,>).MakeGenericType(typeof(Result), responseType), method);
        }

        throw new InvalidOperationException(
            $"Handler '{handlerType.Name}' validates with {validationType.Name} " +
            $"but returns {responseType.Name}. Unsupported validation/response type combination.");
    }

    private static Result<TData, TError> ConvertToDataError<TData, TError>(Result result)
        => Result<TData, TError>.FromError((Result<TError>)result);

    private static Result<TData, TError> ConvertAnyToDataError<TData, TError>(Result result)
        => Result<TData, TError>.FromError(result, (TError)result.ErrorObject!);

    private static TResponse DirectCast<TResponse>(Result result)
        => (TResponse)(object)result;

    private Type[] BuildPipelineBehaviorTypes(Type handlerType, Type requestType, Type responseType)
    {
        var skipSet = handlerType
            .GetCustomAttributes<SkipBehaviorAttribute>(inherit: true)
            .Select(a => a.BehaviorType)
            .ToHashSet();

        var pipeline = new List<Type>();

        foreach (var global in registry.GlobalBehaviors)
        {
            if (global.AppliesTo is not null && !global.AppliesTo(requestType))
                continue;

            if (skipSet.Contains(global.BehaviorType))
                continue;

            var closed = global.BehaviorType.IsGenericTypeDefinition
                ? global.BehaviorType.MakeGenericType(requestType, responseType)
                : global.BehaviorType;

            pipeline.Add(closed);
        }

        foreach (var attr in handlerType.GetCustomAttributes<MediatorBehaviorAttribute>(inherit: true))
        {
            var behaviorType = attr.BehaviorType;
            if (behaviorType.IsGenericTypeDefinition)
                behaviorType = behaviorType.MakeGenericType(requestType, responseType);

            pipeline.Add(behaviorType);
        }

        var expectedInterface = PipelineBehaviorOpenType.MakeGenericType(requestType, responseType);
        foreach (var bt in pipeline)
        {
            if (!expectedInterface.IsAssignableFrom(bt))
                throw new InvalidOperationException(
                    $"Behavior '{bt.Name}' does not implement IPipelineBehavior<{requestType.Name}, {responseType.Name}>.");
        }

        return pipeline.ToArray();
    }
}
