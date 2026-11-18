using System.Text;

namespace Synax.Modules.Internal;

/// <summary>
/// Compares what each module's assemblies contain against what its <c>Configure</c> registered, and
/// throws when the two disagree. This is the half-registered module the module system exists to
/// catch: handlers wired, endpoints never scanned, 404s at runtime instead of an error at startup.
/// </summary>
internal static class ModuleSurfaceValidator
{
    private const int MaxListedTypes = 10;

    public static void Validate(ModuleRegistry registry)
    {
        var failures = new List<string>();

        foreach (var module in registry.Modules)
        foreach (var assembly in module.Assemblies)
        foreach (var provider in SurfaceProviderCache.For(assembly))
        {
            var unregistered = provider
                .Detect(assembly)
                .Where(type => !registry.IsIgnored(type) && !registry.IsClaimed(provider.Capability, type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            if (unregistered.Length > 0)
                failures.Add(Describe(module, provider.Capability, unregistered));
        }

        if (failures.Count == 0)
            return;

        throw new InvalidOperationException(
            $"Synax module validation failed.{Environment.NewLine}{Environment.NewLine}" +
            string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    private static string Describe(ModuleRecord module, SynaxModuleCapability capability, Type[] unregistered)
    {
        var noun = unregistered.Length == 1 ? capability.Surface : capability.Surface + "s";

        var message = new StringBuilder()
            .Append($"Module '{module.Name}' declares {unregistered.Length} {noun} that ")
            .AppendLine(unregistered.Length == 1 ? "is not registered:" : "are not registered:");

        foreach (var type in unregistered.Take(MaxListedTypes))
            message.AppendLine($"  - {type.FullName}");

        if (unregistered.Length > MaxListedTypes)
            message.AppendLine($"  - ... and {unregistered.Length - MaxListedTypes} more");

        return message
            .Append($"Call builder.{capability.RegistrationCall} in {module.Name}.Configure, ")
            .Append($"or builder.Ignore<{unregistered[0].Name}>() to suppress.")
            .ToString();
    }
}
