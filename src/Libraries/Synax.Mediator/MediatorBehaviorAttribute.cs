namespace Synax.Mediator;

/// <summary>
/// Attaches a pipeline behavior to a handler, e.g. <c>[MediatorBehavior(typeof(LoggingBehavior&lt;,&gt;))]</c>.
/// Open-generic behavior types are closed over the handler's request/response types.
/// Inherit from this to create named behavior attributes (e.g. <c>[Audited]</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class MediatorBehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    /// The <see cref="IPipelineBehavior{TRequest,TResponse}"/> type (open generic allowed).
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}
