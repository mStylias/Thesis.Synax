using System.Reflection;

namespace Synax.Modules.Surface;

/// <summary>
/// Finds the types of one <see cref="SynaxModuleCapability"/> inside an assembly, so surface
/// validation can tell whether a module left any of them unregistered.
/// <para>
/// A provider is discovered through <see cref="SynaxModuleSurfaceAttribute"/> on the assembly that
/// declares it, and is applied only to module assemblies that reference that assembly. Detection is
/// therefore scoped by what the module actually compiles against: an assembly that never references
/// Synax.AspNetCore is never checked for unregistered endpoints.
/// </para>
/// </summary>
public interface ISynaxModuleSurfaceProvider
{
    /// <summary>The capability whose types this provider detects.</summary>
    SynaxModuleCapability Capability { get; }

    /// <summary>Returns the types in <paramref name="assembly"/> belonging to the capability.</summary>
    IEnumerable<Type> Detect(Assembly assembly);
}
