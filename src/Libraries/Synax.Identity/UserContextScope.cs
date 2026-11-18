namespace Synax.Identity;

/// <summary>
/// Default <see cref="IUserContextScope"/> backed by an <see cref="AsyncLocal{T}"/>:
/// the injected service is a facade, the override itself is ambient per async flow.
/// </summary>
internal sealed class UserContextScope : IUserContextScope
{
    private static readonly AsyncLocal<UserContextOverride?> Ambient = new();

    public UserContextOverride? Current => Ambient.Value;

    public IDisposable BecomeUser<TKey>(TKey userId) where TKey : notnull
        => Push(new UserContextOverride(userId, userId));

    public IDisposable BecomeUser<TKey>(TKey userId, TKey ownerId) where TKey : notnull
        => Push(new UserContextOverride(userId, ownerId));

    public IDisposable BecomeOwner<TKey>(TKey ownerId) where TKey : notnull
        => Push(new UserContextOverride(null, ownerId));

    public IDisposable BecomeAllOwners()
        => Push(new UserContextOverride { OverridesUser = false, AllOwners = true });

    public IDisposable BecomeSystem()
        => Push(new UserContextOverride());

    private static Restorer Push(UserContextOverride value)
    {
        var previous = Ambient.Value;

        // An override that does not speak for the user (BecomeAllOwners) must not drop the user
        // an enclosing scope established — otherwise nesting it inside BecomeUser would silently
        // fall back to the ambient principal, which in a background host is nobody.
        if (!value.OverridesUser && previous is { OverridesUser: true })
        {
            value = value with { UserId = previous.UserId, OverridesUser = true };
        }

        Ambient.Value = value;
        return new Restorer(previous);
    }

    private sealed class Restorer(UserContextOverride? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
