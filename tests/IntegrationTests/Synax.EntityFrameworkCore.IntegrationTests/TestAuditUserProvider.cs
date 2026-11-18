using Synax.EntityFrameworkCore.Abstractions.Auditing;

namespace Synax.EntityFrameworkCore.IntegrationTests;

public class TestAuditUserProvider : IAuditUserProvider
{
    private readonly AsyncLocal<int?> _currentUserId = new();

    public int? CurrentUserId
    {
        get => _currentUserId.Value;
        set => _currentUserId.Value = value;
    }

    public object? GetCurrentUserId() => CurrentUserId;
}
