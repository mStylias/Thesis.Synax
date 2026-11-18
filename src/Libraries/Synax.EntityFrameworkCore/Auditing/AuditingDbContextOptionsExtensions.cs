using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Auditing;

namespace Synax.EntityFrameworkCore.Auditing;

/// <summary>
/// Extension methods for adding the <see cref="AuditingInterceptor"/> to DbContext options.
/// </summary>
public static class AuditingDbContextOptionsExtensions
{
    /// <summary>
    /// Adds the <see cref="AuditingInterceptor"/>, stamping <c>CreatedAt</c>/<c>CreatedById</c> on
    /// insert and <c>UpdatedAt</c>/<c>ModifiedById</c> on update for entities implementing the
    /// auditing interfaces. An <see cref="IAuditUserProvider"/> and <see cref="TimeProvider"/>
    /// registered in the application's service provider are resolved automatically on each save.
    /// </summary>
    public static DbContextOptionsBuilder AddAuditingInterceptor(
        this DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(new AuditingInterceptor());
        return optionsBuilder;
    }

    /// <summary>
    /// Adds the <see cref="AuditingInterceptor"/> with an explicit <paramref name="userProvider"/>
    /// used for <c>CreatedById</c>/<c>ModifiedById</c> stamping.
    /// </summary>
    public static DbContextOptionsBuilder AddAuditingInterceptor(
        this DbContextOptionsBuilder optionsBuilder,
        IAuditUserProvider userProvider)
    {
        optionsBuilder.AddInterceptors(new AuditingInterceptor(userProvider));
        return optionsBuilder;
    }
}
