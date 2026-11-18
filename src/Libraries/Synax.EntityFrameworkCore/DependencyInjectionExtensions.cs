using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering Synax.EntityFrameworkCore services with the DI container.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers repositories, unit of work, and database manager services configured by <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddSynaxEfCore(
        this IServiceCollection services,
        Action<SynaxEfCoreOptions> configure)
    {
        var options = new SynaxEfCoreOptions();
        configure(options);

        foreach (var dbContextRegistration in options.DbContextRegistrations)
        {
            var contextType = dbContextRegistration.ResolveContextType();
            var dbContextType = dbContextRegistration.DbContextType;

            foreach (var entityType in dbContextRegistration.GetEntityTypes())
            {
                AddRepository(services, contextType, dbContextType, entityType, options.RepositoryLifetime);
            }

            AddUnitOfWork(services, contextType, dbContextType, options.UnitOfWorkLifetime);
            AddDatabaseManager(services, contextType, dbContextType, options.DatabaseManagerLifetime);
            AddContextBinding(services, contextType, dbContextType);

            foreach (var descriptor in dbContextRegistration.ServiceDescriptors)
            {
                services.Add(descriptor);
            }
        }

        return services;
    }

    /// <summary>
    /// Registers a repository for a single entity type, for entities that live outside the
    /// namespaces scanned by <c>AddSynaxEfCore</c> (e.g. library-provided entities such as the
    /// outbox). No-op when the repository is already registered.
    /// </summary>
    public static IServiceCollection AddSynaxRepository<TEntity, TContext, TDbContext>(
        this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TEntity : class, IEntity
        where TContext : ISynaxContext
        where TDbContext : DbContext
    {
        if (services.Any(d => d.ServiceType == typeof(IRepository<TEntity, TContext>)))
            return services;

        AddRepository(services, typeof(TContext), typeof(TDbContext), typeof(TEntity), lifetime);
        return services;
    }

    /// <summary>
    /// Registers a repository for an entity outside the scanned namespaces, resolving the
    /// <c>DbContext</c> behind <typeparamref name="TContext"/> from its
    /// <see cref="SynaxContextBinding{TContext}"/> when the service is first built rather than now.
    /// <para>
    /// This is the overload for code that must not name the concrete context — a module's
    /// application layer wiring the outbox, for example — and it is order-independent: the
    /// binding only has to exist by the time the repository is resolved, not by the time it is
    /// registered.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSynaxRepository<TEntity, TContext>(
        this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TEntity : class, IEntity
        where TContext : ISynaxContext
    {
        if (services.Any(d => d.ServiceType == typeof(IRepository<TEntity, TContext>)))
            return services;

        Type? implementationType = null;

        services.Add(new ServiceDescriptor(
            typeof(IRepository<TEntity, TContext>),
            sp =>
            {
                // Benign race: the computation is deterministic, so a duplicate is harmless.
                implementationType ??= typeof(Repository<,,>).MakeGenericType(
                    typeof(TEntity),
                    typeof(TContext),
                    sp.GetRequiredService<SynaxContextBinding<TContext>>().DbContextType);

                return ActivatorUtilities.CreateInstance(sp, implementationType);
            },
            lifetime));

        return services;
    }

    internal static void AddRepository(
        IServiceCollection services,
        Type contextType,
        Type dbContextType,
        Type entityType,
        ServiceLifetime repositoriesLifetime)
    {
        if (!entityType.IsClass || entityType.IsAbstract)
        {
            throw new ArgumentException("Repository entities must be non-abstract classes.", nameof(entityType));
        }

        var closedInterface = typeof(IRepository<,>).MakeGenericType(entityType, contextType);

        // Registrations are first-wins throughout: a modular app reaches the same DbContext from
        // more than one call, and a duplicate descriptor would show up in every GetServices<> of
        // the interface even though GetRequiredService<> still resolves.
        if (services.Any(descriptor => descriptor.ServiceType == closedInterface))
            return;

        var closedImplementation = typeof(Repository<,,>).MakeGenericType(entityType, contextType, dbContextType);

        services.Add(new ServiceDescriptor(closedInterface, closedImplementation, repositoriesLifetime));
    }

    private static void AddDatabaseManager(
        IServiceCollection services,
        Type contextType,
        Type dbContextType,
        ServiceLifetime lifetime)
    {
        var serviceType = typeof(IDatabaseManager<>).MakeGenericType(contextType);

        if (services.Any(descriptor => descriptor.ServiceType == serviceType))
            return;

        services.Add(new ServiceDescriptor(
            serviceType,
            typeof(DatabaseManager<,>).MakeGenericType(contextType, dbContextType),
            lifetime));
    }

    private static void AddUnitOfWork(
        IServiceCollection services,
        Type contextType,
        Type dbContextType,
        ServiceLifetime lifetime)
    {
        var serviceType = typeof(IUnitOfWork<>).MakeGenericType(contextType);

        if (services.Any(descriptor => descriptor.ServiceType == serviceType))
            return;

        services.Add(new ServiceDescriptor(
            serviceType,
            typeof(UnitOfWork<,>).MakeGenericType(contextType, dbContextType),
            lifetime));
    }

    private static void AddContextBinding(IServiceCollection services, Type contextType, Type dbContextType)
    {
        var bindingType = typeof(SynaxContextBinding<>).MakeGenericType(contextType);

        if (services.Any(d => d.ServiceType == bindingType))
            return;

        services.AddSingleton(bindingType, Activator.CreateInstance(bindingType, dbContextType)!);
    }
}

/// <summary>
/// Options for Synax.EntityFrameworkCore service registration.
/// </summary>
public sealed class SynaxEfCoreOptions
{
    private readonly Dictionary<Type, SynaxDbContextRegistration> _dbContextRegistrations = [];
    private readonly Dictionary<Type, object> _dbContextOptions = [];

    internal IReadOnlyCollection<SynaxDbContextRegistration> DbContextRegistrations => _dbContextRegistrations.Values;

    /// <summary>
    /// Gets or sets the repository service lifetime. Defaults to scoped.
    /// </summary>
    public ServiceLifetime RepositoryLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Gets or sets the unit of work service lifetime. Defaults to scoped.
    /// </summary>
    public ServiceLifetime UnitOfWorkLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Gets or sets the database manager service lifetime. Defaults to scoped.
    /// </summary>
    public ServiceLifetime DatabaseManagerLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Configures entity scanning and feature services for <typeparamref name="TDbContext"/>.
    /// </summary>
    public SynaxDbContextOptions<TDbContext> ForDbContext<TDbContext>()
        where TDbContext : DbContext
    {
        if (_dbContextOptions.TryGetValue(typeof(TDbContext), out var existingOptions))
        {
            return (SynaxDbContextOptions<TDbContext>)existingOptions;
        }

        var registration = new SynaxDbContextRegistration(typeof(TDbContext));
        var options = new SynaxDbContextOptions<TDbContext>(registration);

        _dbContextRegistrations.Add(typeof(TDbContext), registration);
        _dbContextOptions.Add(typeof(TDbContext), options);

        return options;
    }
}
