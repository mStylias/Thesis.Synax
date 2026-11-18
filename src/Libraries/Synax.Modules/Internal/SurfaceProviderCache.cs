using System.Collections.Concurrent;
using System.Reflection;
using Synax.Modules.Surface;

namespace Synax.Modules.Internal;

/// <summary>
/// Resolves the surface providers that apply to a module assembly: those contributed by the
/// assembly itself and by the assemblies it references. The compiler prunes references a project
/// does not actually use, so this is a precise statement of which Synax capabilities the assembly
/// is even capable of containing.
/// </summary>
internal static class SurfaceProviderCache
{
    private static readonly ConcurrentDictionary<Assembly, ISynaxModuleSurfaceProvider[]> Cache = new();

    public static ISynaxModuleSurfaceProvider[] For(Assembly assembly) => Cache.GetOrAdd(assembly, Build);

    private static ISynaxModuleSurfaceProvider[] Build(Assembly assembly)
    {
        var providers = new List<ISynaxModuleSurfaceProvider>();
        var seen = new HashSet<Type>();

        foreach (var candidate in CandidateAssemblies(assembly))
        foreach (var attribute in candidate.GetCustomAttributes<SynaxModuleSurfaceAttribute>())
        {
            if (!seen.Add(attribute.ProviderType))
                continue;

            if (Activator.CreateInstance(attribute.ProviderType) is not ISynaxModuleSurfaceProvider provider)
            {
                throw new InvalidOperationException(
                    $"'{attribute.ProviderType.FullName}' is declared as a module surface provider by " +
                    $"assembly '{candidate.GetName().Name}' but does not implement " +
                    $"{nameof(ISynaxModuleSurfaceProvider)}.");
            }

            providers.Add(provider);
        }

        return [.. providers.OrderBy(provider => provider.Capability.Name, StringComparer.Ordinal)];
    }

    private static IEnumerable<Assembly> CandidateAssemblies(Assembly assembly)
    {
        yield return assembly;

        foreach (var name in assembly.GetReferencedAssemblies())
        {
            Assembly referenced;
            try
            {
                referenced = Assembly.Load(name);
            }
            catch (Exception exception) when (exception is FileNotFoundException
                                                  or FileLoadException
                                                  or BadImageFormatException)
            {
                continue;
            }

            yield return referenced;
        }
    }
}
