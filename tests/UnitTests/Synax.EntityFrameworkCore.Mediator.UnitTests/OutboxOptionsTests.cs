using Synax.EntityFrameworkCore.Mediator.UnitTests.TestFixtures.Outbox;

namespace Synax.EntityFrameworkCore.Mediator.UnitTests;

public class OutboxOptionsTests
{
    [Test]
    public async Task MapNotification_WhenNameAlreadyMappedToDifferentType_ShouldThrow()
    {
        var options = new OutboxOptions();
        options.MapNotification<OutboxTestNotification>("shared-name");

        var exception = Assert.Throws<ArgumentException>(
            () => options.MapNotification<AlwaysFailingNotification>("shared-name"));

        await Assert.That(exception.Message).Contains("shared-name");
        await Assert.That(exception.Message).Contains(typeof(OutboxTestNotification).FullName!);
    }

    [Test]
    public async Task MapNotification_WhenRemappingSameTypeToSameName_ShouldNotThrow()
    {
        var options = new OutboxOptions();
        options.MapNotification<OutboxTestNotification>("stable-name");
        options.MapNotification<OutboxTestNotification>("stable-name");

        await Assert.That(true).IsTrue(); // no exception is the assertion
    }

    [Test]
    public async Task DefaultBackoff_ShouldBeMonotonicallyNonDecreasingAndCappedAtOneHour()
    {
        var options = new OutboxOptions();

        var previous = TimeSpan.Zero;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var delay = options.Backoff(attempt);
            await Assert.That(delay >= previous).IsTrue();
            await Assert.That(delay <= TimeSpan.FromHours(1)).IsTrue();
            previous = delay;
        }

        await Assert.That(options.Backoff(1)).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(options.Backoff(20)).IsEqualTo(TimeSpan.FromHours(1));
    }
}
