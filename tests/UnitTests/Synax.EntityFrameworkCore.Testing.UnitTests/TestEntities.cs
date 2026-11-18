using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.Mapper.Core;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>Dummy context type parameter — never instantiated by the fakes.</summary>
public sealed class TestDbContext : DbContext, ISynaxContext;

public class Widget : IEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public WidgetChild? Child { get; set; }
}

public class WidgetChild
{
    public string Label { get; set; } = string.Empty;
}

public class WidgetDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class WidgetProfile : MapperProfile
{
    public WidgetProfile()
    {
        CreateMap<Widget, WidgetDto>();
    }
}

public class UserEntity
{
    public Guid Id { get; set; }
}

public class SoftWidget : IEntity, ISoftDelete, IHasDeletionTime, IDeletedBy<UserEntity, Guid?>
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public UserEntity? DeletedBy { get; set; }
}

public class OwnedWidget : IEntity, IHasOwner<int>
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int OwnerId { get; set; }
}

public class SharedWidget : IEntity, IHasOptionalOwner<int?>
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int? OwnerId { get; set; }
}

public class AuditedWidget : IEntity, IAuditedEntity<UserEntity, Guid?>
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public UserEntity? CreatedBy { get; set; }
    public Guid? ModifiedById { get; set; }
    public UserEntity? ModifiedBy { get; set; }
}

public class AuditedSoftWidget : IEntity, IHasAuditTimes, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>A <see cref="TimeProvider"/> pinned to a fixed instant.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
