using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.EntityFrameworkCore.Core;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: <c>SynaxQuery.From(...)</c> — the one-liner for stubbing an
/// <c>ISynaxQuery&lt;T&gt;</c> over in-memory data. The EF async materializers evaluate
/// synchronously with LINQ to Objects, so any consumer of a canned query can be exercised
/// without a repository, provider, or database.
/// </summary>
public class CannedQueryTests
{
    private static readonly Table[] Tables =
    [
        new() { Id = Guid.NewGuid(), Number = 1, Seats = 2, Location = TableLocation.Indoor },
        new() { Id = Guid.NewGuid(), Number = 2, Seats = 4, Location = TableLocation.Outdoor },
        new() { Id = Guid.NewGuid(), Number = 3, Seats = 6, Location = TableLocation.Terrace }
    ];

    [Test]
    public async Task CountAsync_EvaluatesTheCannedSource()
    {
        var query = SynaxQuery.From(Tables);

        await Assert.That(await query.CountAsync()).IsEqualTo(3);
        await Assert.That(await query.CountAsync(t => t.Seats >= 4)).IsEqualTo(2);
    }

    [Test]
    public async Task FirstOrDefaultAsync_ReturnsMatchOrNull()
    {
        var query = SynaxQuery.From(Tables);

        var match = await query.FirstOrDefaultAsync(t => t.Number == 2);
        var missing = await query.FirstOrDefaultAsync(t => t.Number == 99);

        await Assert.That(match!.Location).IsEqualTo(TableLocation.Outdoor);
        await Assert.That(missing).IsNull();
    }
}
