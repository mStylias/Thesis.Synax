using Synax.AspNetCore.HealthChecks;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator;

namespace Synax.BookingDemoApp.Infrastructure.HealthChecks;

/// <summary>
/// Watches the transactional outbox table: dead-lettered rows (attempts exhausted) degrade the
/// service. The outbox table is queried through a regular Synax repository —
/// <c>AddSynaxOutbox</c> registers one for <c>OutboxMessage&lt;BookingContext&gt;</c>.
/// </summary>
public class OutboxHealthCheck(IUnitOfWork<BookingContext> unitOfWork) : ISynaxHealthCheck
{
    public string Name => "Outbox";

    public async Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var deadLettered = await unitOfWork.GetRepository<OutboxMessage<BookingContext>>()
            .Query(q => q.Where(m => m.Attempts >= OutboxDemoDefaults.MaxAttempts))
            .CountAsync(cancellationToken);

        return deadLettered == 0
            ? SynaxHealthCheckResult.Healthy(Name)
            : SynaxHealthCheckResult.Degraded(
                Name, $"{deadLettered} dead-lettered outbox message(s) need attention.");
    }
}
