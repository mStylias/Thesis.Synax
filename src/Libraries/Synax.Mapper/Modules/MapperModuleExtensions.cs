using System.Reflection;
using Synax.Mapper.Core;
using Synax.Mapper.Modules;
using Synax.Modules;
using Synax.Modules.Surface;

[assembly: SynaxModuleSurface(typeof(ProfileSurfaceProvider))]

namespace Synax.Mapper.Modules;

/// <summary>
/// The mapper's contribution to <see cref="ISynaxModuleBuilder"/>. Profiles merge into one shared
/// configuration, so each module registers only its own.
/// </summary>
public static class MapperModuleExtensions
{
    /// <summary>The module capability covering mapper profiles.</summary>
    public static readonly SynaxModuleCapability Profiles =
        new("Profiles", "mapper profile", "AddProfiles()");

    /// <summary>
    /// Scans the module's assemblies for <see cref="MapperProfile"/> implementations. Pass
    /// <paramref name="assemblies"/> to scan a subset instead.
    /// </summary>
    public static ISynaxModuleBuilder AddProfiles(
        this ISynaxModuleBuilder builder, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Defer(services =>
        {
            var targets = assemblies.Length > 0 ? assemblies : [.. builder.Assemblies];

            services.AddSynaxMapper(targets);

            foreach (var assembly in targets)
                builder.Claim(Profiles, assembly);
        });
    }
}

/// <summary>Reports the mapper profiles a module assembly contains.</summary>
internal sealed class ProfileSurfaceProvider : SynaxModuleSurfaceProvider
{
    public override SynaxModuleCapability Capability => MapperModuleExtensions.Profiles;

    protected override bool IsSurfaceType(Type type) => type.IsSubclassOf(typeof(MapperProfile));
}
