namespace Synax.Modules.Internal;

internal static class ModuleActivator
{
    /// <summary>
    /// Instantiates a module. Modules are created during registration, before any service provider
    /// exists, so the only supported shape is a public parameterless constructor.
    /// </summary>
    public static ISynaxModule Create(Type moduleType)
    {
        if (!typeof(ISynaxModule).IsAssignableFrom(moduleType))
            throw new ArgumentException($"'{moduleType.FullName}' does not implement ISynaxModule.", nameof(moduleType));

        if (moduleType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Module '{moduleType.FullName}' has no public parameterless constructor. Modules are " +
                "instantiated during registration, before any service provider exists — read settings from " +
                "ISynaxModuleBuilder.Configuration, or construct the module yourself and pass it to " +
                "AddModule(ISynaxModule).");
        }

        return (ISynaxModule)Activator.CreateInstance(moduleType)!;
    }
}
