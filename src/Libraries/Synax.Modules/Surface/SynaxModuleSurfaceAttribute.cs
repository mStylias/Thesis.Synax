namespace Synax.Modules.Surface;

/// <summary>
/// Declares that the annotated assembly contributes a surface provider to module validation.
/// Applied by the Synax libraries themselves:
/// <code>
/// [assembly: SynaxModuleSurface(typeof(EndpointSurfaceProvider))]
/// </code>
/// A module assembly is checked with the providers contributed by the assemblies it references,
/// which is what keeps validation from demanding capabilities the module never took a dependency on.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SynaxModuleSurfaceAttribute(Type providerType) : Attribute
{
    /// <summary>
    /// The <see cref="ISynaxModuleSurfaceProvider"/> implementation, which must be concrete and
    /// have a public parameterless constructor.
    /// </summary>
    public Type ProviderType { get; } = providerType;
}
