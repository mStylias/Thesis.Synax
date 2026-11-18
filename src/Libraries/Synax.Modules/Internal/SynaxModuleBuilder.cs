using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Synax.Modules.Internal;

internal sealed class SynaxModuleBuilder(
    ModuleRegistry registry,
    ModuleRecord record,
    IServiceCollection services,
    IConfiguration? configuration,
    bool validationEnabled)
    : ISynaxModuleBuilder
{
    public Type ModuleType => record.ModuleType;

    public string ModuleName => record.Name;

    public IServiceCollection Services => services;

    public IConfiguration? Configuration => configuration;

    public IReadOnlyList<Assembly> Assemblies => record.Assemblies;

    public bool ValidationEnabled => validationEnabled;

    public ISynaxModuleBuilder DependsOn<TModule>() where TModule : ISynaxModule => DependsOn(typeof(TModule));

    public ISynaxModuleBuilder DependsOn(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);

        if (!record.Dependencies.Contains(moduleType))
            record.Dependencies.Add(moduleType);

        return this;
    }

    public ISynaxModuleBuilder IncludeAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        record.AddAssembly(assembly);
        return this;
    }

    public ISynaxModuleBuilder IncludeAssemblyOf<T>() => IncludeAssembly(typeof(T).Assembly);

    public ISynaxModuleBuilder Ignore(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        foreach (var type in types)
            registry.Ignore(type);

        return this;
    }

    public ISynaxModuleBuilder Ignore<T>() => Ignore(typeof(T));

    public ISynaxModuleBuilder AddServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(services);
        return this;
    }

    public ISynaxModuleBuilder Defer(Action<IServiceCollection> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        record.Deferred.Add(registration);
        return this;
    }

    public ISynaxModuleBuilder DeferLast(Action<IServiceCollection> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        record.DeferredLast.Add(registration);
        return this;
    }

    public ISynaxModuleBuilder Claim(SynaxModuleCapability capability, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(assembly);

        registry.ClaimAssembly(capability, assembly);
        return this;
    }

    public ISynaxModuleBuilder Claim(SynaxModuleCapability capability, IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(types);

        registry.ClaimTypes(capability, types);
        return this;
    }
}
