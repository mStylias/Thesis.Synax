using System.Runtime.CompilerServices;
using Synax.Results;

namespace Synax.Mediator.Internal;

/// <summary>
/// Holds each handler instance's latest validation result so <c>HandleAsync</c> can read the
/// validated state via <c>this.ValidationData()</c>. Keyed weakly by the (scoped, per-request)
/// handler instance — no leaks, no cross-request bleed.
/// </summary>
internal static class ValidationResultStore
{
    private static readonly ConditionalWeakTable<object, Result> Store = new();

    internal static void Set(object handler, Result result) => Store.AddOrUpdate(handler, result);

    internal static Result? Get(object handler) => Store.TryGetValue(handler, out var result) ? result : null;
}
