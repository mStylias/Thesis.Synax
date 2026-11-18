using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mapper.Core;

namespace Synax.Crud.UnitTests;

/// <summary>Dummy context type parameter — never instantiated by the fakes.</summary>
public sealed class TestDbContext : DbContext, ISynaxContext;

public class Gadget : IEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Stock { get; set; }
}

public class GadgetResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Stock { get; set; }
}

public class GadgetProfile : MapperProfile
{
    public GadgetProfile()
    {
        CreateMap<Gadget, GadgetResponse>();
    }
}
