namespace Synax.BookingDemoApp.Domain;

/// <summary>
/// Outbox tuning values shared between the registration (<c>AddSynaxOutbox</c> in Application)
/// and the outbox health check, so "dead-lettered" means the same thing in both places.
/// </summary>
public static class OutboxDemoDefaults
{
    public const int MaxAttempts = 5;
}
