using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.SoftDelete;

public static class SoftDeleteDbContextOptionsExtensions
{
    /// <summary>
    /// Enables the whole soft-delete feature with a single call: adds the
    /// <see cref="SoftDeleteInterceptor"/> (converting deletes on <see cref="ISoftDelete"/>
    /// entities into soft deletes) <b>and</b> applies the soft-delete query filters to the model
    /// automatically via a convention — no <c>ApplySoftDeleteQueryFilters()</c> call in
    /// <c>OnModelCreating</c> is required (a manual call is still allowed and takes precedence).
    /// <para>
    /// If an <see cref="ISoftDeleteUserProvider"/> is registered in the application's service
    /// provider, it is resolved automatically on each save to populate
    /// <see cref="IDeletedBy{TUser,TUserKey}.DeletedById"/> — no explicit instance is required.
    /// </para>
    /// <para>
    /// To add the interceptor <i>without</i> the automatic query filters, use
    /// <c>optionsBuilder.AddInterceptors(new SoftDeleteInterceptor())</c> directly.
    /// </para>
    /// </summary>
    public static DbContextOptionsBuilder AddSoftDeleteInterceptor(
        this DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(new SoftDeleteInterceptor());
        return optionsBuilder.AddSoftDeleteExtension();
    }

    /// <summary>
    /// Same as <see cref="AddSoftDeleteInterceptor(DbContextOptionsBuilder)"/> but with an explicit
    /// <paramref name="userProvider"/> used to populate
    /// <see cref="IDeletedBy{TUser,TUserKey}.DeletedById"/> during soft delete operations.
    /// </summary>
    public static DbContextOptionsBuilder AddSoftDeleteInterceptor(
        this DbContextOptionsBuilder optionsBuilder,
        ISoftDeleteUserProvider userProvider)
    {
        optionsBuilder.AddInterceptors(new SoftDeleteInterceptor(userProvider));
        return optionsBuilder.AddSoftDeleteExtension();
    }

    private static DbContextOptionsBuilder AddSoftDeleteExtension(this DbContextOptionsBuilder optionsBuilder)
    {
        var extension = optionsBuilder.Options.FindExtension<SoftDeleteOptionsExtension>()
            ?? new SoftDeleteOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return optionsBuilder;
    }
}
