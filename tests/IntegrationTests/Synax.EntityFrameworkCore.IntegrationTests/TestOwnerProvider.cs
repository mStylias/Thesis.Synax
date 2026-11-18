using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.EntityFrameworkCore.IntegrationTests;

public class TestOwnerProvider : IOwnerProvider<int>
{
    private readonly AsyncLocal<int?> _currentOwnerId = new();

    public int? CurrentOwnerId
    {
        get => _currentOwnerId.Value;
        set => _currentOwnerId.Value = value;
    }

    private readonly AsyncLocal<bool> _crossOwner = new();

    /// <summary>Mirrors <c>IUserContextScope.BecomeAllOwners</c> without pulling in the scope.</summary>
    public bool CrossOwner
    {
        get => _crossOwner.Value;
        set => _crossOwner.Value = value;
    }

    public object? GetCurrentOwnerId() => CurrentOwnerId;

    public bool IsCrossOwner() => CrossOwner;
}
