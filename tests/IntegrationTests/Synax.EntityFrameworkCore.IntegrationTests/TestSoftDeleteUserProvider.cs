using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests;

public class TestSoftDeleteUserProvider : ISoftDeleteUserProvider
{
    private readonly AsyncLocal<int> _currentUserId = new();

    public int CurrentUserId
    {
        get => _currentUserId.Value;
        set => _currentUserId.Value = value;
    }

    public object GetCurrentUserId() => CurrentUserId;
}
