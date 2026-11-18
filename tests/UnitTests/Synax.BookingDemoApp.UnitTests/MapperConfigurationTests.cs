using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Application.Contracts.Bookings;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.BookingDemoApp.UnitTests.TestSupport;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: <c>MapperConfiguration.AssertConfigurationIsValid()</c> as the config-drift guard —
/// the demo app runs it at startup in Development, and this test keeps it green in CI — plus
/// spot checks of the runtime mapper (enum→string automapping and <c>MapList</c>).
/// </summary>
public class MapperConfigurationTests
{
    [Test]
    public async Task FullDemoConfiguration_IsValid()
    {
        var configuration = TestWorld.BuildMapperConfiguration();

        // Throws MappingValidationException listing every unmapped destination member if any
        // profile drifted from its entity/request shape.
        configuration.AssertConfigurationIsValid();

        await Assert.That(configuration.CreateMapper()).IsNotNull();
    }

    [Test]
    public async Task Map_BookingToResponse_ConvertsEnumToString()
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            CustomerName = "Alan Turing",
            CustomerEmail = "alan@example.com",
            Date = new DateTimeOffset(2026, 9, 1, 20, 0, 0, TimeSpan.Zero),
            PartySize = 4,
            Status = BookingStatus.Cancelled,
            Notes = "birthday"
        };

        var response = TestWorld.Mapper.Map<BookingResponse>(booking);

        await Assert.That(response.Id).IsEqualTo(booking.Id);
        await Assert.That(response.Status).IsEqualTo("Cancelled");
        await Assert.That(response.CustomerName).IsEqualTo("Alan Turing");
        await Assert.That(response.PartySize).IsEqualTo(4);
    }

    [Test]
    public async Task MapList_ProjectsEveryElement()
    {
        var tables = new[]
        {
            new Table { Id = Guid.NewGuid(), Number = 1, Seats = 2, Location = TableLocation.Bar },
            new Table { Id = Guid.NewGuid(), Number = 2, Seats = 8, Location = TableLocation.PrivateRoom }
        };

        var responses = TestWorld.Mapper.MapList<TableResponse>(tables);

        await Assert.That(responses).Count().IsEqualTo(2);
        await Assert.That(responses[0].Location).IsEqualTo("Bar");
        await Assert.That(responses[1].Location).IsEqualTo("PrivateRoom");
        await Assert.That(responses[1].Seats).IsEqualTo(8);
    }
}
