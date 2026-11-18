using Synax.Mapper.Core;

namespace Synax.Mapper.Exceptions;

/// <summary>
/// Thrown by <see cref="MapperConfiguration.AssertConfigurationIsValid"/> when one or
/// more mappings leave destination members unbound. The message lists every offending
/// map and member; <see cref="InvalidMaps"/> exposes the same information structurally.
/// </summary>
public sealed class MappingValidationException : InvalidOperationException
{
    /// <summary>
    /// The maps that have unmapped destination members. Inspect
    /// <see cref="TypeMap.UnmappedMembers"/> on each entry for the member names.
    /// </summary>
    public IReadOnlyList<TypeMap> InvalidMaps { get; }

    internal MappingValidationException(IReadOnlyList<TypeMap> invalidMaps)
        : base(BuildMessage(invalidMaps))
    {
        InvalidMaps = invalidMaps;
    }

    private static string BuildMessage(IReadOnlyList<TypeMap> invalidMaps)
    {
        var lines = invalidMaps.Select(map =>
            $"  '{map.SourceType.Name}' → '{map.DestinationType.Name}': " +
            string.Join(", ", map.UnmappedMembers));

        return "The following mappings have destination members that nothing maps to. " +
               "Bind them with .For(...), exclude them with .Ignore(...), or add matching " +
               "source properties:" + Environment.NewLine +
               string.Join(Environment.NewLine, lines);
    }
}
