namespace Synax.Mapper.Exceptions;

/// <summary>
/// Thrown when a requested mapping between two types has not been configured.
/// </summary>
public sealed class MappingNotFoundException : InvalidOperationException
{
    public Type SourceType { get; }
    public Type DestinationType { get; }

    public MappingNotFoundException(Type sourceType, Type destinationType)
        : base($"No mapping configured from '{sourceType.FullName}' to '{destinationType.FullName}'. " +
               $"Register it in a MapperProfile using CreateMap<{sourceType.Name}, {destinationType.Name}>().")
    {
        SourceType = sourceType;
        DestinationType = destinationType;
    }
}
