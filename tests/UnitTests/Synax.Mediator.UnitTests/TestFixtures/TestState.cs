using System.Collections.Concurrent;

namespace Synax.Mediator.UnitTests.TestFixtures;

public sealed class HandlerSpy
{
    public ConcurrentQueue<string> ExecutedHandlers { get; } = new();
    public CancellationToken LastReceivedToken { get; set; }
}
