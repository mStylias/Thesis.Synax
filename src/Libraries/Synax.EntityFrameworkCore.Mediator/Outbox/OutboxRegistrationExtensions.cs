using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Synax.EntityFrameworkCore.Abstractions.Core;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Registration for the transactional outbox. Three explicit touch points:
/// <code>
/// optionsBuilder.AddOutboxInterceptor&lt;BookingContext, BookingDbContext&gt;();  // DbContext options
/// modelBuilder.AddSynaxOutbox&lt;BookingContext&gt;();                             // OnModelCreating (owns the migration)
/// services.AddSynaxOutbox&lt;BookingContext&gt;(o =&gt; { ... });                     // DI
/// </code>
/// Only the interceptor names the concrete <c>DbContext</c>, and it is configured on the
/// context's own options — so a module's application layer can wire its outbox (including the
/// notification-name mappings, which live with the notifications) without referencing the
/// assembly that defines the context.
/// </summary>
public static class OutboxRegistrationExtensions
{
    /// <summary>
    /// Registers the outbox services: <see cref="IOutbox{TContext}"/> for enqueueing,
    /// the in-process hot-path channel, and the dispatcher hosted service (channel pump +
    /// recovery sweep). Pair with <c>AddOutboxInterceptor</c> on the DbContext options and
    /// <c>AddSynaxOutbox</c> on the model builder.
    /// <para>
    /// The outbox table's repository binds to whichever <c>DbContext</c> <typeparamref name="TContext"/>
    /// was mapped to by <c>AddSynaxEfCore</c>, resolved when the repository is first built — so this
    /// call may run before or after the one that registers the context.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSynaxOutbox<TContext>(
        this IServiceCollection services, Action<OutboxOptions>? configure = null)
        where TContext : ISynaxContext
    {
        var options = new OutboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(new OutboxOptionsHolder<TContext>(options));
        services.TryAddSingleton<OutboxChannel<TContext>>();
        services.TryAddScoped<OutboxPendingBuffer<TContext>>();
        services.TryAddScoped<IOutbox<TContext>, Outbox<TContext>>();
        services.AddSynaxRepository<OutboxMessage<TContext>, TContext>();

        // TryAddEnumerable, not AddHostedService: a second call for the same context would
        // otherwise start a second dispatcher pump against the same table.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, OutboxDispatcher<TContext>>());

        return services;
    }

    /// <summary>
    /// Adds the <see cref="OutboxInterceptor{TContext,TDbContext}"/>, which signals the dispatcher
    /// strictly after outbox rows become durable (post-save, or post-commit when a
    /// transaction is active).
    /// </summary>
    public static DbContextOptionsBuilder AddOutboxInterceptor<TContext, TDbContext>(
        this DbContextOptionsBuilder optionsBuilder)
        where TContext : ISynaxContext
        where TDbContext : DbContext
        => optionsBuilder.AddInterceptors(new OutboxInterceptor<TContext, TDbContext>());

    /// <summary>
    /// Configures the <see cref="OutboxMessage{TContext}"/> entity (table <c>SynaxOutbox</c>).
    /// Call from <c>OnModelCreating</c> — the app owns the resulting migration.
    /// </summary>
    public static ModelBuilder AddSynaxOutbox<TContext>(this ModelBuilder modelBuilder)
        where TContext : ISynaxContext
    {
        var entity = modelBuilder.Entity<OutboxMessage<TContext>>();
        entity.ToTable("SynaxOutbox");
        entity.HasKey(m => m.Id);
        entity.Property(m => m.Type).HasMaxLength(512).IsRequired();
        entity.Property(m => m.Payload).IsRequired();
        // The sweep filters on due time (or creation age for never-attempted rows) — both indexed.
        entity.HasIndex(m => m.NextAttemptAt);
        entity.HasIndex(m => m.CreatedAt);

        return modelBuilder;
    }
}
