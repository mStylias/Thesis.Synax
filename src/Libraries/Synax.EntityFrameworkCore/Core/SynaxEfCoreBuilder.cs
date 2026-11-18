using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.EntityFrameworkCore.Core;

/// <summary>
/// Configures entity namespace scanning and feature services for a DbContext.
/// </summary>
public sealed class SynaxDbContextOptions<TDbContext>
    where TDbContext : DbContext
{
    private readonly SynaxDbContextRegistration _registration;

    internal SynaxDbContextOptions(SynaxDbContextRegistration registration)
    {
        _registration = registration;
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for all <see cref="IEntity"/> classes in <paramref name="namespaceName"/>
    /// and its child namespaces, then correlates them with <typeparamref name="TDbContext"/>.
    /// </summary>
    public SynaxDbContextOptions<TDbContext> ScanNamespace(
        Assembly assembly,
        string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            throw new ArgumentException("Namespace must be provided.", nameof(namespaceName));
        }

        _registration.NamespaceRegistrations.Add(new EntityNamespaceRegistration(assembly, namespaceName));
        return this;
    }

    /// <summary>
    /// Scans the namespace of <typeparamref name="TEntity"/> and its child namespaces, then correlates
    /// discovered <see cref="IEntity"/> classes with <typeparamref name="TDbContext"/>.
    /// </summary>
    public SynaxDbContextOptions<TDbContext> ScanNamespaceOf<TEntity>()
    {
        var namespaceName = typeof(TEntity).Namespace
            ?? throw new InvalidOperationException($"{typeof(TEntity).FullName} does not declare a namespace.");

        return ScanNamespace(typeof(TEntity).Assembly, namespaceName);
    }

    /// <summary>
    /// Scans the whole of <paramref name="assembly"/> for <see cref="IEntity"/> classes, whatever
    /// namespace they sit in, and correlates them with <typeparamref name="TDbContext"/>. This is
    /// what the module system uses: a module already states which assemblies it owns, so it has no
    /// namespace to repeat.
    /// </summary>
    public SynaxDbContextOptions<TDbContext> ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _registration.NamespaceRegistrations.Add(new EntityNamespaceRegistration(assembly, namespaceName: null));
        return this;
    }

    /// <summary>The entity types the scans configured so far resolve to.</summary>
    internal IReadOnlyCollection<Type> EntityTypes => _registration.GetEntityTypes();

    /// <summary>
    /// Binds this <c>DbContext</c> to a standalone <see cref="ISynaxContext"/> tag, so
    /// <see cref="IUnitOfWork{TContext}"/> / <see cref="IRepository{TEntity,TContext}"/> are
    /// resolved as <typeparamref name="TContext"/> instead of <typeparamref name="TDbContext"/>.
    /// <para>
    /// Declare the tag wherever the consuming code already looks — a module's domain or contracts
    /// project — and its handlers no longer need a reference to the assembly holding the
    /// <c>DbContext</c>:
    /// <code>
    /// public sealed class BillingContext : ISynaxContext;          // contracts
    ///
    /// options.ForDbContext&lt;BillingDbContext&gt;()                   // infrastructure
    ///        .As&lt;BillingContext&gt;()
    ///        .ScanNamespaceOf&lt;Invoice&gt;();
    ///
    /// IUnitOfWork&lt;BillingContext&gt; unitOfWork                      // application
    /// </code>
    /// </para>
    /// Without this call the <c>DbContext</c> is its own tag and must therefore implement
    /// <see cref="ISynaxContext"/> itself.
    /// </summary>
    public SynaxDbContextOptions<TDbContext> As<TContext>()
        where TContext : ISynaxContext
    {
        if (_registration.ContextType is { } existing && existing != typeof(TContext))
        {
            throw new InvalidOperationException(
                $"'{typeof(TDbContext).Name}' is already bound to the context tag '{existing.Name}'. " +
                "A DbContext maps to exactly one tag.");
        }

        _registration.ContextType = typeof(TContext);
        return this;
    }

    /// <summary>
    /// Registers an <see cref="IOwnerProvider{TKey}"/> implementation for ownership-scoped
    /// persistence and querying.
    /// </summary>
    public SynaxDbContextOptions<TDbContext> WithOwnership<TKey>(
        Action<OwnershipOptions<TKey>> configure)
        where TKey : notnull
    {
        var options = new OwnershipOptions<TKey>();
        configure(options);

        if (options.ProviderType is null)
        {
            throw new InvalidOperationException(
                "An IOwnerProvider implementation must be specified via UseProvider<T>().");
        }

        _registration.ServiceDescriptors.Add(new ServiceDescriptor(
            typeof(IOwnerProvider<TKey>),
            options.ProviderType,
            options.Lifetime));

        return this;
    }
}

internal sealed class SynaxDbContextRegistration(Type dbContextType)
{
    public Type DbContextType { get; } = dbContextType;

    /// <summary>The tag set by <c>As&lt;TContext&gt;()</c>; <c>null</c> means the DbContext is its own tag.</summary>
    public Type? ContextType { get; set; }

    public List<EntityNamespaceRegistration> NamespaceRegistrations { get; } = [];

    public List<ServiceDescriptor> ServiceDescriptors { get; } = [];

    /// <summary>
    /// The type Synax services are keyed by. Defaults to the <c>DbContext</c> itself, which then
    /// has to implement <see cref="ISynaxContext"/> — the alternative being an explicit tag.
    /// </summary>
    public Type ResolveContextType()
    {
        if (ContextType is not null)
            return ContextType;

        if (!typeof(ISynaxContext).IsAssignableFrom(DbContextType))
        {
            throw new InvalidOperationException(
                $"'{DbContextType.Name}' is registered without a context tag, so it acts as its own — " +
                $"declare it as 'public class {DbContextType.Name} : DbContext, ISynaxContext', or bind a " +
                $"standalone tag with ForDbContext<{DbContextType.Name}>().As<TContext>().");
        }

        return DbContextType;
    }

    public IReadOnlyCollection<Type> GetEntityTypes()
    {
        return NamespaceRegistrations
            .SelectMany(registration => registration.GetEntityTypes())
            .Distinct()
            .ToArray();
    }
}

/// <summary><paramref name="namespaceName"/> of <c>null</c> means the whole assembly.</summary>
internal sealed class EntityNamespaceRegistration(Assembly assembly, string? namespaceName)
{
    public IReadOnlyCollection<Type> GetEntityTypes()
    {
        return assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(IEntity).IsAssignableFrom(type))
            .Where(type => IsInNamespace(type, namespaceName))
            .ToArray();
    }

    private static bool IsInNamespace(Type type, string? namespaceName)
    {
        return namespaceName is null
            || type.Namespace == namespaceName
            || type.Namespace?.StartsWith(namespaceName + ".", StringComparison.Ordinal) == true;
    }
}

/// <summary>
/// Configuration options for the ownership feature.
/// </summary>
public sealed class OwnershipOptions<TKey>
    where TKey : notnull
{
    internal Type? ProviderType;
    internal ServiceLifetime Lifetime = ServiceLifetime.Scoped;

    /// <summary>
    /// Specifies the <see cref="IOwnerProvider{TKey}"/> implementation to use.
    /// </summary>
    /// <param name="lifetime">
    /// The service lifetime. Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </param>
    public void UseProvider<TProvider>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TProvider : class, IOwnerProvider<TKey>
    {
        ProviderType = typeof(TProvider);
        Lifetime = lifetime;
    }
}
