namespace Synax.Mediator.Internal;

/// <summary>
/// A global behavior plus its optional startup-time request-type filter.
/// </summary>
internal readonly record struct GlobalBehaviorRegistration(Type BehaviorType, Func<Type, bool>? AppliesTo);
