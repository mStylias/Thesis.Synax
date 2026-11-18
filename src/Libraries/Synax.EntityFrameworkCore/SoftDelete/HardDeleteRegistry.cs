namespace Synax.EntityFrameworkCore.SoftDelete;

/// <summary>
/// Context-scoped registry of entities explicitly marked for hard (permanent) deletion.
/// Registered as a scoped service in EF's internal provider by <see cref="SoftDeleteOptionsExtension"/>,
/// so each DbContext instance owns its marks — no process-wide static state.
/// </summary>
internal sealed class HardDeleteRegistry
{
    /// <summary>
    /// Reference equality: entities overriding Equals/GetHashCode (e.g. records with value
    /// equality) must not cause a mark on one instance to apply to an equal-but-distinct one.
    /// </summary>
    internal HashSet<object> Entities { get; } = new(ReferenceEqualityComparer.Instance);
}
