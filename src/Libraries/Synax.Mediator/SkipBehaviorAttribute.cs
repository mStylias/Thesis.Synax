namespace Synax.Mediator;

/// <summary>
/// Excludes a global pipeline behavior from this handler.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SkipBehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>
    /// The global behavior type to skip (must match the type registered via <see cref="MediatorOptions.AddGlobalBehavior"/>).
    /// </summary>
    public Type BehaviorType { get; } = behaviorType;
}
