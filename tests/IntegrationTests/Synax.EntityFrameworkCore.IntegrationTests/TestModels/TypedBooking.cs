using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

public enum BookingStatus
{
    Pending,
    Confirmed,
    Cancelled
}

/// <summary>
/// Exercises bulk insert against columns whose store types differ from a naive CLR-type mapping:
/// <see cref="DateTime"/> (Npgsql maps to <c>timestamp with time zone</c>), enums (default int
/// conversion), enums with an explicit string conversion, and <c>numeric</c> with facets.
/// </summary>
public class TypedBooking : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public BookingStatus Status { get; set; }
    public BookingStatus StatusText { get; set; }
    public decimal Price { get; set; }
}
