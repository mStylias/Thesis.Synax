using System.Reflection;

namespace Synax.Mediator.Internal;

/// <summary>
/// Shared mediator registration state: populated at startup, read-only at runtime.
/// The first <c>AddSynaxMediator</c> call registers the instance with the container; later
/// calls find it on the service collection and merge into it, so modules can register
/// independently.
/// <para>
/// Handler discovery and pipeline construction are deliberately separate. Discovery records a
/// <see cref="HandlerRegistration"/>; pipeline construction folds in the
/// <see cref="GlobalBehaviors"/> accumulated so far. Because every registration call rebuilds
/// every pipeline, a behavior registered by one module reaches handlers discovered by another
/// regardless of which module registered first.
/// </para>
/// Request pipelines are stored as <c>object</c> because <c>RequestPipeline&lt;TResponse&gt;</c> varies by TResponse.
/// </summary>
internal sealed class HandlerRegistry
{
    private readonly Dictionary<Type, object> _requestPipelines = [];
    private readonly Dictionary<Type, HandlerRegistration> _handlers = [];
    private readonly Dictionary<Type, NotificationPipeline> _notificationPipelines = [];
    private readonly HashSet<Assembly> _scannedAssemblies = [];
    private readonly List<GlobalBehaviorRegistration> _globalBehaviors = [];
    private readonly HashSet<Type> _registeredBehaviorTypes = [];

    // ---------- Global behaviors ----------

    /// <summary>
    /// The global behaviors accumulated across every <c>AddSynaxMediator</c> call, in the order
    /// they were first registered.
    /// </summary>
    internal IReadOnlyList<GlobalBehaviorRegistration> GlobalBehaviors => _globalBehaviors;

    /// <summary>
    /// Adds a global behavior, or widens the scope of one already registered for the same
    /// behavior type. A behavior type attaches to a pipeline at most once: re-registering it
    /// ORs the predicates together, and an unconditional registration subsumes any predicate.
    /// Two modules independently registering the same behavior therefore do not run it twice.
    /// The behavior keeps the position of its first registration, so pipeline order stays
    /// deterministic.
    /// </summary>
    internal void AddGlobalBehavior(GlobalBehaviorRegistration behavior)
    {
        var index = _globalBehaviors.FindIndex(b => b.BehaviorType == behavior.BehaviorType);

        if (index < 0)
        {
            _globalBehaviors.Add(behavior);
            return;
        }

        var existing = _globalBehaviors[index];

        if (existing.AppliesTo is null || behavior.AppliesTo is null)
        {
            // One of the two registrations is unconditional, which subsumes the other's predicate.
            _globalBehaviors[index] = existing with { AppliesTo = null };
            return;
        }

        var (first, second) = (existing.AppliesTo, behavior.AppliesTo);
        _globalBehaviors[index] = existing with
        {
            AppliesTo = requestType => first(requestType) || second(requestType)
        };
    }

    /// <summary>
    /// Marks a behavior's DI descriptor as handled. Returns <c>false</c> when an earlier
    /// pipeline build already registered it, so rebuilding pipelines does not rescan the
    /// service collection once per call.
    /// </summary>
    internal bool TryMarkBehaviorRegistered(Type behaviorType)
        => _registeredBehaviorTypes.Add(behaviorType);

    // ---------- Handlers ----------

    /// <summary>The recorded handlers, one per request type.</summary>
    internal IEnumerable<HandlerRegistration> Handlers => _handlers.Values;

    /// <summary>
    /// Records a discovered handler. The caller guarantees the request has no handler yet
    /// (see <see cref="TryGetRequestHandlerType"/>), so a collision here is a broken invariant.
    /// </summary>
    internal void AddHandler(HandlerRegistration handler)
        => _handlers.Add(handler.RequestType, handler);

    internal bool TryGetRequestHandlerType(Type requestType, out Type handlerType)
    {
        if (_handlers.TryGetValue(requestType, out var registration))
        {
            handlerType = registration.HandlerType;
            return true;
        }

        handlerType = null!;
        return false;
    }

    // ---------- Request pipelines ----------

    internal void SetRequestPipeline(Type requestType, object pipeline)
        => _requestPipelines[requestType] = pipeline;

    internal RequestPipeline<TResponse> GetRequestPipeline<TResponse>(Type requestType)
    {
        if (_requestPipelines.TryGetValue(requestType, out var pipeline))
            return (RequestPipeline<TResponse>)pipeline;

        throw new InvalidOperationException(
            $"No handler registered for request type '{requestType.Name}'. " +
            $"Ensure a handler implementing IRequestHandler<{requestType.Name}, TResponse> is registered.");
    }

    // ---------- Notification pipelines ----------

    internal void RegisterNotificationPipeline(Type notificationType, NotificationPipeline pipeline)
        => _notificationPipelines[notificationType] = pipeline;

    internal bool HasNotificationPipeline(Type notificationType)
        => _notificationPipelines.ContainsKey(notificationType);

    internal NotificationPipeline GetNotificationPipeline(Type notificationType)
    {
        if (_notificationPipelines.TryGetValue(notificationType, out var pipeline))
            return pipeline;

        throw new InvalidOperationException(
            $"No handler registered for notification type '{notificationType.Name}'. " +
            $"Ensure a handler implementing INotificationHandler<{notificationType.Name}> is registered.");
    }

    // ---------- Assembly scanning ----------

    /// <summary>
    /// Marks the assembly as scanned. Returns <c>false</c> if it was already scanned,
    /// so the same assembly registered multiple times (or across multiple
    /// <c>AddSynaxMediator</c> calls) is only processed once.
    /// </summary>
    internal bool TryMarkAssemblyScanned(Assembly assembly)
        => _scannedAssemblies.Add(assembly);
}
