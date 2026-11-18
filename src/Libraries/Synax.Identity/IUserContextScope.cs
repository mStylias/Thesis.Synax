namespace Synax.Identity;

/// <summary>
/// Runs a block of code as a specific user, owner, across every owner, or as "system"
/// (explicitly nobody), overriding the ambient principal — for background jobs, seeding,
/// outbox dispatch, or admin operations. Scopes nest (innermost wins entirely) and flow
/// correctly across <c>await</c>, so parallel branches can hold different scopes without
/// cross-talk.
/// <para>
/// This abstraction has no ASP.NET Core dependency by design: the ambient override is just
/// an <c>AsyncLocal</c>, and application layers, workers and seeders need it as much as a
/// web host does. <c>ICurrentUser</c> (Synax.AspNetCore) and the ownership providers
/// (Synax.EntityFrameworkCore) both consult it.
/// </para>
/// </summary>
/// <example>
/// <code>
/// using (userContext.BecomeOwner(tenantId))   // owner = tenant, user = none
///     await RecalculateStatsAsync(ct);
///
/// using (userContext.BecomeUser(userId))      // user = userId, owner follows (= userId)
///     await sender.SendAsync(new CancelBooking { Id = id }, ct);
///
/// using (userContext.BecomeAllOwners())       // every owner's rows, current user preserved
///     await ReportAcrossTenantsAsync(ct);
///
/// using (userContext.BecomeSystem())          // explicitly nobody, even mid-request
///     await SeedSharedDataAsync(ct);
/// </code>
/// </example>
public interface IUserContextScope
{
    /// <summary>
    /// Acts as <paramref name="userId"/> for the scope's duration; the owner follows the user.
    /// The key type is inferred from the argument.
    /// </summary>
    IDisposable BecomeUser<TKey>(TKey userId) where TKey : notnull;

    /// <summary>
    /// Acts as <paramref name="userId"/> with an independent <paramref name="ownerId"/>
    /// (e.g. user within a tenant) for the scope's duration.
    /// </summary>
    IDisposable BecomeUser<TKey>(TKey userId, TKey ownerId) where TKey : notnull;

    /// <summary>
    /// Acts as owner <paramref name="ownerId"/> (e.g. a tenant) with no user for the scope's
    /// duration — ownership-scoped queries see that owner's rows, audit stamps stay null.
    /// </summary>
    IDisposable BecomeOwner<TKey>(TKey ownerId) where TKey : notnull;

    /// <summary>
    /// Lifts owner scoping entirely for the scope's duration: ownership-scoped queries see
    /// <b>every</b> owner's rows, not one owner's and not only shared ones. For cross-tenant
    /// reporting, admin tooling and maintenance sweeps that would otherwise have to loop
    /// owner-by-owner.
    /// <para>
    /// Unlike the other members this one does <b>not</b> replace the user: the ambient
    /// principal (or an enclosing scope's user) still stamps audit columns, which is what an
    /// admin acting across tenants wants. Nesting a <c>BecomeOwner</c>/<c>BecomeSystem</c>
    /// inside narrows back down as usual.
    /// </para>
    /// <para>
    /// <b>Writes are not widened.</b> There is no ambient owner to stamp, so inserting an
    /// entity with a required owner inside this scope throws unless <c>OwnerId</c> is set
    /// explicitly — cross-owner reads must not silently produce mis-owned rows.
    /// </para>
    /// <para>
    /// Requires the ownership query filters to have been given a cross-owner accessor —
    /// <c>modelBuilder.ApplyOwnershipQueryFilters&lt;TKey&gt;(() =&gt; ownerProvider.GetCurrentOwnerId(),
    /// () =&gt; ownerProvider.IsCrossOwner())</c>. Without the second argument the filter has no
    /// term to widen and this scope affects reads not at all.
    /// </para>
    /// </summary>
    IDisposable BecomeAllOwners();

    /// <summary>
    /// Acts as explicitly nobody — no user, no owner — even when a real principal is ambient.
    /// Ownership-scoped queries see only shared rows; nothing is stamped.
    /// </summary>
    IDisposable BecomeSystem();

    /// <summary>
    /// The active override, or <c>null</c> when none is in effect (normal HTTP flow).
    /// Consulted by <c>ICurrentUser</c> and the EF ownership providers; application code
    /// rarely needs it.
    /// </summary>
    UserContextOverride? Current { get; }
}

/// <summary>
/// An active identity override. Replaces the ambient principal for everything it specifies —
/// inside a scope that overrides the user, claim-based configuration (user id selector, owner
/// claim) does not apply, because there is no principal to select from.
/// </summary>
public sealed record UserContextOverride
{
    /// <summary>Creates an override that specifies neither user nor owner.</summary>
    public UserContextOverride()
    {
    }

    /// <summary>Creates an override replacing the user and the owner with the given keys.</summary>
    public UserContextOverride(object? userId, object? ownerId)
    {
        UserId = userId;
        OwnerId = ownerId;
    }

    /// <summary>The boxed user key, or <c>null</c> for "no user".</summary>
    public object? UserId { get; init; }

    /// <summary>
    /// Whether this override speaks for the user at all. <c>false</c> only for
    /// <see cref="IUserContextScope.BecomeAllOwners"/>, which changes owner scoping and leaves
    /// the ambient principal to supply the user.
    /// </summary>
    public bool OverridesUser { get; init; } = true;

    /// <summary>The boxed owner key, or <c>null</c> for "no owner".</summary>
    public object? OwnerId { get; init; }

    /// <summary>
    /// Whether owner scoping is lifted for this scope — every owner's rows are visible and
    /// <see cref="OwnerId"/> is meaningless. See <see cref="IUserContextScope.BecomeAllOwners"/>.
    /// </summary>
    public bool AllOwners { get; init; }
}
