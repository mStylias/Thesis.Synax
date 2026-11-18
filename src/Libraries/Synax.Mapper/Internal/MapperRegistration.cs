using System.Reflection;
using Synax.Mapper.Core;

namespace Synax.Mapper.Internal;

/// <summary>
/// Accumulates the profiles contributed by every <c>AddSynaxMapper</c> call and builds the
/// single <see cref="MapperConfiguration"/> the container hands out.
/// <para>
/// A <see cref="MapperConfiguration"/> freezes its maps at construction and cannot absorb a
/// second one, so building eagerly per call would make the last registration silently replace
/// the earlier ones. Profiles are therefore collected into one shared
/// <see cref="MapperConfigurationExpression"/> — which keeps duplicate detection at registration
/// time — and compiled once, on first resolution.
/// </para>
/// </summary>
internal sealed class MapperRegistration
{
    private readonly Lock _gate = new();
    private readonly MapperConfigurationExpression _expression = new();
    private readonly HashSet<Assembly> _scannedAssemblies = [];
    private readonly HashSet<Type> _profileTypes = [];
    private MapperConfiguration? _configuration;

    internal void Add(Action<MapperConfigurationExpression> configure)
    {
        lock (_gate)
        {
            ThrowIfBuilt();
            configure(_expression);
        }
    }

    /// <summary>
    /// Adds every <see cref="MapperProfile"/> in the given assemblies. Assemblies and profile
    /// types already contributed are skipped, so modules that scan an assembly their neighbour
    /// also scans do not collide on a duplicate mapping.
    /// </summary>
    internal void AddAssemblies(IEnumerable<Assembly> assemblies)
    {
        lock (_gate)
        {
            ThrowIfBuilt();

            foreach (var assembly in assemblies)
            {
                if (!_scannedAssemblies.Add(assembly))
                    continue;

                var profileTypes = assembly.GetTypes()
                    .Where(t => t is { IsAbstract: false, IsClass: true }
                                && t.IsSubclassOf(typeof(MapperProfile)));

                foreach (var profileType in profileTypes)
                {
                    if (!_profileTypes.Add(profileType))
                        continue;

                    _expression.AddProfile((MapperProfile)Activator.CreateInstance(profileType)!);
                }
            }
        }
    }

    internal MapperConfiguration Build()
    {
        lock (_gate)
        {
            return _configuration ??= new MapperConfiguration(_expression);
        }
    }

    private void ThrowIfBuilt()
    {
        if (_configuration is null)
            return;

        throw new InvalidOperationException(
            "AddSynaxMapper was called after the MapperConfiguration had already been built. " +
            "Profiles must be registered before the first IMapper/MapperConfiguration is resolved — " +
            "move the call into service registration.");
    }
}
