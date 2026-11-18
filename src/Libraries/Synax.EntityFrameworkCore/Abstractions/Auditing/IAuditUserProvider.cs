namespace Synax.EntityFrameworkCore.Abstractions.Auditing;

/// <summary>
/// Supplies the current user's identifier for audit stamping (<c>CreatedById</c>,
/// <c>ModifiedById</c>). Register an implementation in the application's service provider, or pass
/// an instance to <c>AddAuditingInterceptor</c>. Returning <c>null</c> skips user stamping (the
/// timestamps are still written).
/// </summary>
public interface IAuditUserProvider
{
    /// <summary>The current user's identifier, or <c>null</c> when no user is available.</summary>
    object? GetCurrentUserId();
}
