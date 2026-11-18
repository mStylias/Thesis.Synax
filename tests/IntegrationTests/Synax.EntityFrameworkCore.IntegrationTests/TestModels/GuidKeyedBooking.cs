using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

/// <summary>
/// Guid primary key with EF's default client-side generation (ValueGenerated.OnAdd, no store
/// default) — exercises bulk insert's client value generation.
/// </summary>
public class GuidKeyedBooking : IEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
