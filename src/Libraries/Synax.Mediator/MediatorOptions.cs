using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Synax.Mediator.Internal;
using Synax.Results.Logging;

namespace Synax.Mediator;

/// <summary>
/// Configuration for the Synax mediator, including handler registration, lifetime, and pipeline behaviors.
/// </summary>
public sealed class MediatorOptions
{
    /// <summary>
    /// The DI lifetime for handlers and behaviors. Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </summary>
    public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// The default strategy used to publish notifications. Defaults to <see cref="NotificationPublishStrategy.Sequential"/>.
    /// </summary>
    public NotificationPublishStrategy NotificationPublishStrategy { get; set; } = NotificationPublishStrategy.Sequential;

    internal List<Assembly> Assemblies { get; } = [];
    internal List<Type> ExplicitHandlerTypes { get; } = [];
    internal List<GlobalBehaviorRegistration> GlobalBehaviors { get; } = [];
    internal ResultLogOptions? ResultLogOptions { get; private set; }

    /// <summary>
    /// Registers all handlers found in the specified <paramref name="assembly"/>.
    /// </summary>
    public MediatorOptions AddHandlersFromAssembly(Assembly assembly)
    {
        Assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers all handlers found in the assembly containing <typeparamref name="T"/>.
    /// </summary>
    public MediatorOptions AddHandlersFromAssemblyContaining<T>()
    {
        Assemblies.Add(typeof(T).Assembly);
        return this;
    }

    /// <summary>
    /// Registers a single closed handler type — the hook for generated/generic handlers
    /// (e.g. Synax.Crud) that assembly scanning cannot discover. Explicit handlers are
    /// registered <b>after</b> the scanned assemblies of the same call, and are <b>skipped</b>
    /// when their request already has a handler — so a handwritten handler for the same
    /// request always wins over an explicitly-added default. Duplicate explicit additions
    /// of the same handler type are idempotent.
    /// </summary>
    /// <param name="closedHandlerType">
    /// A concrete, fully-closed type implementing a handler interface
    /// (<see cref="ICommandHandler{TRequest,TResponse}"/>, <see cref="IQueryHandler{TRequest,TResponse}"/>, ...).
    /// </param>
    public MediatorOptions AddHandler(Type closedHandlerType)
    {
        ArgumentNullException.ThrowIfNull(closedHandlerType);

        if (closedHandlerType.IsAbstract || closedHandlerType.IsInterface || closedHandlerType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"'{closedHandlerType.Name}' cannot be registered as a handler: the type must be " +
                "concrete and fully closed (no open generic parameters).", nameof(closedHandlerType));
        }

        if (!ExplicitHandlerTypes.Contains(closedHandlerType))
            ExplicitHandlerTypes.Add(closedHandlerType);

        return this;
    }

    /// <summary>
    /// Registers an open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/> that runs on every request,
    /// or — when <paramref name="appliesTo"/> is given — only on requests whose type matches the predicate
    /// (evaluated once at startup). Use <see cref="SkipBehaviorAttribute"/> on individual handlers to opt out.
    /// <para>
    /// Behaviors are <b>global to the application</b>, not to the <c>AddSynaxMediator</c> call that
    /// registered them: they attach to every handler, including those discovered by other calls,
    /// whether those calls ran before or after this one. Modules can therefore register their
    /// handlers and their cross-cutting behaviors independently and in any order.
    /// </para>
    /// <para>
    /// A behavior type attaches to a pipeline at most once. Registering the same type again
    /// widens its scope — the predicates are OR-ed, and an unconditional registration wins over
    /// any predicate — so two modules both asking for, say, result logging do not log twice.
    /// The behavior keeps the position of its first registration in the pipeline order.
    /// </para>
    /// </summary>
    /// <param name="openGenericBehaviorType">The behavior type (open generic allowed).</param>
    /// <param name="appliesTo">
    /// Optional startup-time filter on the request type, e.g.
    /// <c>t =&gt; typeof(IMyMarker).IsAssignableFrom(t)</c> to scope the behavior to marked requests.
    /// </param>
    public MediatorOptions AddGlobalBehavior(Type openGenericBehaviorType, Func<Type, bool>? appliesTo = null)
    {
        GlobalBehaviors.Add(new GlobalBehaviorRegistration(openGenericBehaviorType, appliesTo));
        return this;
    }

    /// <summary>
    /// Registers <see cref="ResultLoggingBehavior{TRequest,TResponse}"/> on every request:
    /// failed and warning <see cref="Synax.Results.Result"/> responses are logged automatically
    /// (messages, origin, attached exception; severity from the result status). Successful
    /// results are not logged.
    /// <para>
    /// Safe to call from more than one module: the behavior is registered once application-wide
    /// (see <see cref="AddGlobalBehavior"/>), so results are never logged twice.
    /// </para>
    /// </summary>
    /// <param name="logOptions">
    /// Optional field configuration passed to <c>LogResult</c>; when <c>null</c>, all fields are included.
    /// </param>
    public MediatorOptions AddResultLogging(ResultLogOptions? logOptions = null)
    {
        ResultLogOptions = logOptions;
        return AddGlobalBehavior(typeof(ResultLoggingBehavior<,>));
    }
}

/// <summary>
/// Controls how notifications are dispatched to their handlers.
/// </summary>
public enum NotificationPublishStrategy
{
    /// <summary>
    /// Handlers are invoked one at a time in registration order.
    /// </summary>
    Sequential,

    /// <summary>
    /// All handlers are invoked concurrently.
    /// </summary>
    Parallel
}
