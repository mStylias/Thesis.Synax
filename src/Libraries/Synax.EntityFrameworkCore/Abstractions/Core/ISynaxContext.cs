namespace Synax.EntityFrameworkCore.Abstractions.Core;

/// <summary>
/// Identifies a persistence context. Synax's consumption surface —
/// <see cref="IUnitOfWork{TContext}"/>, <see cref="IRepository{TEntity,TContext}"/>,
/// <see cref="IDatabaseManager{TContext}"/> — is keyed by this type rather than by a
/// <c>DbContext</c>, which is what keeps each context's services distinct while letting the
/// consuming code stay free of Entity Framework wiring.
/// <para>
/// Two ways to supply it:
/// </para>
/// <list type="number">
///   <item>
///     <b>The DbContext is its own tag.</b> Declare
///     <c>public class AppDbContext : DbContext, ISynaxContext</c> and use
///     <c>IUnitOfWork&lt;AppDbContext&gt;</c>. Nothing else to write — the right default for an
///     application with a single context.
///   </item>
///   <item>
///     <b>A standalone tag.</b> Declare <c>public sealed class BillingContext : ISynaxContext;</c>
///     in a project the application layer already references, bind it at registration with
///     <c>ForDbContext&lt;BillingDbContext&gt;().As&lt;BillingContext&gt;()</c>, and use
///     <c>IUnitOfWork&lt;BillingContext&gt;</c>. Handlers then name a type their own module owns,
///     so a module's application code no longer has to reference the assembly that defines its
///     <c>DbContext</c> — the pattern to reach for in a modular monolith.
///   </item>
/// </list>
/// Either way the type argument is unique per context, so one module cannot resolve another
/// module's repositories.
/// </summary>
public interface ISynaxContext;
