using System.Reflection;

namespace Synax.Modules.Surface;

/// <summary>
/// Base class for surface providers that identify their types by a predicate. Handles the
/// type enumeration, including assemblies whose types cannot all be loaded.
/// </summary>
public abstract class SynaxModuleSurfaceProvider : ISynaxModuleSurfaceProvider
{
    /// <inheritdoc />
    public abstract SynaxModuleCapability Capability { get; }

    /// <inheritdoc />
    public IEnumerable<Type> Detect(Assembly assembly)
        => GetLoadableTypes(assembly)
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .Where(IsSurfaceType);

    /// <summary>Whether <paramref name="type"/> is part of this capability's surface.</summary>
    protected abstract bool IsSurfaceType(Type type);

    /// <summary>
    /// The types of <paramref name="assembly"/>, degrading to the loadable subset rather than
    /// throwing when a dependency is missing.
    /// </summary>
    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
