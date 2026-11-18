using Synax.BookingDemoApp.Application.Features.Tables;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.UnitTests.TestSupport;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: the warning status of Synax.Results (<c>Result.Warn</c> still succeeds but carries
/// a server-side message) and <c>QuerySelect</c> — an inline projection expression evaluated by
/// the fake's LINQ-to-Objects provider, no mapper involved.
/// </summary>
public class GetTablesWarningTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid RestaurantId = Guid.NewGuid();

    [Test]
    public async Task RestaurantWithNoTables_SucceedsAsWarning_WithMessage()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant);
        var handler = new GetTablesHandler(uow);

        var result = await handler.HandleAsync(
            new GetTablesRequest { RestaurantId = RestaurantId }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.IsWarning).IsTrue();
        await Assert.That(result.Data!).IsEmpty();
        await Assert.That(result.Messages).Contains("Restaurant has no tables yet.");
    }

    [Test]
    public async Task RestaurantWithTables_ReturnsPlainSuccess_WithEnumRenderedAsString()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant);
        uow.Seed(
            new Table
            {
                Id = Guid.NewGuid(), RestaurantId = RestaurantId, Number = 2, Seats = 4,
                Location = TableLocation.Terrace, OwnerId = Tenant
            },
            new Table
            {
                Id = Guid.NewGuid(), RestaurantId = RestaurantId, Number = 1, Seats = 2,
                Location = TableLocation.Indoor, OwnerId = Tenant
            },
            // A different restaurant's table — filtered out by the handler's Where clause.
            new Table
            {
                Id = Guid.NewGuid(), RestaurantId = Guid.NewGuid(), Number = 9, Seats = 8,
                Location = TableLocation.Bar, OwnerId = Tenant
            });
        var handler = new GetTablesHandler(uow);

        var result = await handler.HandleAsync(
            new GetTablesRequest { RestaurantId = RestaurantId }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.IsWarning).IsFalse();
        await Assert.That(result.Data!).Count().IsEqualTo(2);
        // Ordered by table number, with the enum converted to its display string.
        await Assert.That(result.Data![0].Number).IsEqualTo(1);
        await Assert.That(result.Data![0].Location).IsEqualTo("Indoor");
        await Assert.That(result.Data![1].Location).IsEqualTo("Terrace");
    }
}
