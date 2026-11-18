using System.Reflection;
using Microsoft.Extensions.Configuration;
using Synax.Modules.Internal;
using Synax.Modules.Surface;

namespace Synax.Modules;

/// <summary>Configures a call to <c>AddSynaxModules</c>.</summary>
public sealed class SynaxModuleOptions
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<Type> _moduleTypes = [];
    private readonly List<ISynaxModule> _instances = [];

    /// <summary>
    /// Whether to verify, once every module has registered, that no Synax surface type in a
    /// module's assemblies was left unregistered. Defaults to <c>true</c>.
    /// <para>
    /// Validation runs against the modules registered so far, so turn it off if the host registers
    /// part of a module's surface itself after this call — or exclude the individual types with
    /// <see cref="ISynaxModuleBuilder.Ignore(Type[])"/>.
    /// </para>
    /// </summary>
    public bool ValidateSurface { get; set; } = true;

    /// <summary>
    /// Configuration handed to each module through <see cref="ISynaxModuleBuilder.Configuration"/>.
    /// </summary>
    public IConfiguration? Configuration { get; set; }

    /// <summary>Adds every <see cref="ISynaxModule"/> implementation in <paramref name="assembly"/>.</summary>
    public SynaxModuleOptions AddModulesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!_assemblies.Contains(assembly))
            _assemblies.Add(assembly);

        return this;
    }

    /// <inheritdoc cref="AddModulesFromAssembly"/>
    public SynaxModuleOptions AddModulesFromAssemblyContaining<T>()
        => AddModulesFromAssembly(typeof(T).Assembly);

    /// <summary>Adds a single module by type.</summary>
    public SynaxModuleOptions AddModule<TModule>() where TModule : ISynaxModule
        => AddModule(typeof(TModule));

    /// <inheritdoc cref="AddModule{TModule}"/>
    public SynaxModuleOptions AddModule(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);

        if (!typeof(ISynaxModule).IsAssignableFrom(moduleType))
            throw new ArgumentException($"'{moduleType.FullName}' does not implement ISynaxModule.", nameof(moduleType));

        if (!_moduleTypes.Contains(moduleType))
            _moduleTypes.Add(moduleType);

        return this;
    }

    /// <summary>
    /// Adds an already-constructed module — the way to give a module constructor dependencies the
    /// scanning path cannot supply.
    /// </summary>
    public SynaxModuleOptions AddModule(ISynaxModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _instances.Add(module);
        return this;
    }

    /// <summary>
    /// Materializes the modules in a deterministic order: scanned assemblies in the order given
    /// (their modules ordered by full name), then explicit types, then instances.
    /// </summary>
    internal IReadOnlyList<ISynaxModule> ResolveModules()
    {
        var modules = new List<ISynaxModule>();

        foreach (var assembly in _assemblies)
        {
            var moduleTypes = SynaxModuleSurfaceProvider
                .GetLoadableTypes(assembly)
                .Where(IsModuleType)
                .OrderBy(type => type.FullName, StringComparer.Ordinal);

            foreach (var moduleType in moduleTypes)
                modules.Add(ModuleActivator.Create(moduleType));
        }

        foreach (var moduleType in _moduleTypes)
            modules.Add(ModuleActivator.Create(moduleType));

        modules.AddRange(_instances);

        if (modules.Count == 0)
            throw new InvalidOperationException(BuildNoModulesMessage());

        return modules;
    }

    private static bool IsModuleType(Type type)
        => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
           && typeof(ISynaxModule).IsAssignableFrom(type);

    private string BuildNoModulesMessage()
    {
        if (_assemblies.Count == 0)
            return "AddSynaxModules was called without any modules. Add one with AddModulesFromAssembly, " +
                   "AddModule<TModule>(), or AddModule(ISynaxModule).";

        var names = string.Join(", ", _assemblies.Select(assembly => $"'{assembly.GetName().Name}'"));
        return $"No ISynaxModule implementation was found in {names}. A module is a non-abstract class " +
               "implementing ISynaxModule with a public parameterless constructor.";
    }
}
