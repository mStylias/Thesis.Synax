using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.Core;

/// <summary>
/// Records which <c>DbContext</c> a context tag was bound to by <c>AddSynaxEfCore</c>.
/// Registered as a singleton per tag, it lets registrations made <b>outside</b> the layer that
/// knows the concrete context — a module's application layer registering the outbox, for
/// instance — defer resolving the <c>DbContext</c> type until the service is first built.
/// </summary>
public sealed class SynaxContextBinding<TContext>
    where TContext : ISynaxContext
{
    public SynaxContextBinding(Type dbContextType)
    {
        if (!typeof(DbContext).IsAssignableFrom(dbContextType))
        {
            throw new ArgumentException(
                $"'{dbContextType.Name}' is not a DbContext.", nameof(dbContextType));
        }

        DbContextType = dbContextType;
    }

    /// <summary>The <c>DbContext</c> type <typeparamref name="TContext"/> resolves to.</summary>
    public Type DbContextType { get; }
}
