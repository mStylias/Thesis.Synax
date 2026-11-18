namespace Synax.EntityFrameworkCore.Abstractions.Auditing;

/// <summary>
/// Combines <see cref="IHasCreationTime"/> and <see cref="IHasModificationTime"/> — timestamp-only
/// auditing without user tracking.
/// </summary>
public interface IHasAuditTimes : IHasCreationTime, IHasModificationTime
{
}
