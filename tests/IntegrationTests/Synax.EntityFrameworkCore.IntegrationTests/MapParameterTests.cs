using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;
using Synax.Mapper.Core;
using Synax.Mapper.Core.Extensions;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// A profile's per-call value against a real provider: it must reach SQL as a parameter, so one
/// compiled query serves every value, rather than as a literal baked into a plan per value.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class MapParameterTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task QueryDto_WhenTheParameterIsSupplied_ShouldReadItInSql()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync();
        var bookingRepository = unitOfWork.GetRepository<Booking>();
        await bookingRepository.AddAsync(new Booking { Id = 7101, Name = "Window" });
        await bookingRepository.AddAsync(new Booking { Id = 7102, Name = "Terrace" });
        await unitOfWork.SaveChangesAsync();

        // Act
        var rows = await bookingRepository
            .QueryDto<RankedBookingViewDto>(
                query => query.Where(b => b.Id >= 7101 && b.Id <= 7102),
                parameters: new MapParameters { ["preferredName"] = "Terrace" })
            .ToListAsync();

        // Assert
        await Assert.That(rows.Single(r => r.IsPreferred).Id).IsEqualTo(7102);
        await Assert.That(rows.Count(r => r.IsPreferred)).IsEqualTo(1);
    }

    [Test]
    public async Task ProjectTo_WhenTheParameterIsSupplied_ShouldSendItAsASqlParameter()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();

        // Act
        var sql = dbContext.Bookings
            .ProjectTo<RankedBookingViewDto>(mapper, new MapParameters { ["preferredName"] = "Marker-7201" })
            .ToQueryString();

        // Assert
        // ToQueryString lists parameter values in a leading comment; the statement is what matters.
        var statement = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        await Assert.That(statement).DoesNotContain("Marker-7201").Because("a literal would compile one plan per value");
        await Assert.That(statement).Contains("\"Name\" = @");
    }

    [Test]
    public async Task QueryDto_WhenCalledWithTwoValues_ShouldCompileTheQueryOnce()
    {
        // Arrange
        const string marker = "7301";
        QueryCompilationCounter.Instance.Track(marker);
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        // Act
        var first = await bookingRepository
            .QueryDto<RankedBookingViewDto>(
                query => query.Where(b => b.Id == 7301),
                parameters: new MapParameters { ["preferredName"] = "Window" })
            .ToListAsync();
        var second = await bookingRepository
            .QueryDto<RankedBookingViewDto>(
                query => query.Where(b => b.Id == 7301),
                parameters: new MapParameters { ["preferredName"] = "Terrace" })
            .ToListAsync();

        // Assert
        await Assert.That(first).IsEmpty();
        await Assert.That(second).IsEmpty();
        await Assert.That(QueryCompilationCounter.Instance.GetCompilations(marker)).IsEqualTo(1);
    }

    [Test]
    public async Task QueryDto_WhenTheParameterIsMissing_ShouldThrowBeforeQuerying()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        // Act & Assert
        await Assert.That(() => bookingRepository.QueryDto<RankedBookingViewDto>())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'preferredName'");
    }
}
