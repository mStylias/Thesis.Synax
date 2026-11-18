using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Synax.Modules.Internal;

/// <summary>
/// The cross-call accumulator for module registration, parked on the service collection as a
/// singleton instance — the same pattern <c>HandlerRegistry</c> and <c>MapperRegistration</c> use,
/// so several <c>AddSynaxModules</c> calls compose and nothing lives in static state.
/// </summary>
internal sealed class ModuleRegistry
{
    private readonly Dictionary<(Type Type, string Name), ModuleRecord> _modules = [];
    private readonly List<ModuleRecord> _ordered = [];
    private readonly Dictionary<SynaxModuleCapability, CapabilityClaims> _claims = [];
    private readonly HashSet<Type> _ignored = [];

    public IReadOnlyList<ModuleRecord> Modules => _ordered;

    /// <summary>
    /// Adds a module, or returns <c>null</c> when it was already registered — listing the same
    /// module twice must not configure it twice. Identity is the module's type and name; since
    /// <see cref="ISynaxModule.Name"/> defaults to the type name, that is plain type identity
    /// unless a module deliberately names its instances.
    /// </summary>
    public ModuleRecord? TryAdd(Type moduleType, string name)
    {
        var key = (moduleType, name);

        if (_modules.ContainsKey(key))
            return null;

        var record = new ModuleRecord(moduleType, name);
        _modules.Add(key, record);
        _ordered.Add(record);
        return record;
    }

    public void Ignore(Type type) => _ignored.Add(type);

    public bool IsIgnored(Type type) => _ignored.Contains(type);

    public void ClaimAssembly(SynaxModuleCapability capability, Assembly assembly)
        => GetClaims(capability).Assemblies.Add(assembly);

    public void ClaimTypes(SynaxModuleCapability capability, IEnumerable<Type> types)
    {
        var claims = GetClaims(capability);
        foreach (var type in types)
            claims.Types.Add(type);
    }

    /// <summary>
    /// Claims are pooled across modules rather than kept per module: two modules may legitimately
    /// share an assembly, and the one that registered it settles the question for both.
    /// </summary>
    public bool IsClaimed(SynaxModuleCapability capability, Type type)
        => _claims.TryGetValue(capability, out var claims)
           && (claims.Assemblies.Contains(type.Assembly) || claims.Types.Contains(type));

    private CapabilityClaims GetClaims(SynaxModuleCapability capability)
    {
        if (_claims.TryGetValue(capability, out var existing))
            return existing;

        var claims = new CapabilityClaims();
        _claims.Add(capability, claims);
        return claims;
    }

    private sealed class CapabilityClaims
    {
        public HashSet<Assembly> Assemblies { get; } = [];

        public HashSet<Type> Types { get; } = [];
    }
}

internal sealed class ModuleRecord(Type moduleType, string name)
{
    private readonly List<Assembly> _assemblies = [];

    public Type ModuleType { get; } = moduleType;

    public string Name { get; } = name;

    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    public List<Type> Dependencies { get; } = [];

    public List<Action<IServiceCollection>> Deferred { get; } = [];

    public List<Action<IServiceCollection>> DeferredLast { get; } = [];

    public void AddAssembly(Assembly assembly)
    {
        if (!_assemblies.Contains(assembly))
            _assemblies.Add(assembly);
    }
}
