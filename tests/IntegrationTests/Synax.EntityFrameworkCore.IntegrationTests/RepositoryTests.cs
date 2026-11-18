using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class RepositoryTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task QueryAsync_WhenEntityExists_ShouldReturnEntity()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync();
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        await bookingRepository.AddAsync(
            new Booking { Id = 1, Name = "Test name", Description = "Test" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await bookingRepository
            .Query()
            .ToListAsync();

        // Assert
        await Assert.That(result).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CreateDtoQueryable_WhenEntityExists_ShouldMapToDto()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync();
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        await bookingRepository.AddAsync(
            new Booking { Id = 2, Name = "Test name 2", Description = "Test" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await bookingRepository
            .QueryDto<ReferenceViewDto>(query => query.Where(b => b.Id == 2))
            .ToListAsync();

        // Assert
        await Assert.That(result[0].Id).IsEqualTo(2);
        await Assert.That(result[0].Name).IsEqualTo("Test name 2");
    }

    [Test]
    public async Task CreatePropertiesQueryable_WhenEntityExists_ShouldSelectProperties()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync();
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        await bookingRepository.AddAsync(
            new Booking { Id = 3, Name = "Properties test", Description = "Should not appear" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await bookingRepository
            .QuerySelect(
                b => new { b.Id, b.Name },
                q => q.Where(b => b.Id == 3))
            .SingleAsync();

        // Assert
        await Assert.That(result.Id).IsEqualTo(3);
        await Assert.That(result.Name).IsEqualTo("Properties test");
    }

    [Test]
    public async Task CreatePropertiesQueryable_WhenSelectingSingleProperty_ShouldReturnScalar()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync();
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        await bookingRepository.AddAsync(
            new Booking { Id = 4, Name = "Scalar test", Description = "Desc" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var name = await bookingRepository
            .QuerySelect(b => b.Name, q => q.Where(b => b.Id == 4))
            .SingleAsync();

        // Assert
        await Assert.That(name).IsEqualTo("Scalar test");
    }

    [Test]
    public async Task BulkInsertAsync_When500Entities_ShouldInsertAll()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync();
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        var bookings = Enumerable.Range(1, 500)
            .Select(i => new Booking { Name = $"Bulk booking {i}", Description = $"Description {i}" })
            .ToList();

        // Act
        var startTime = Stopwatch.GetTimestamp();
        await bookingRepository.BulkInsertAsync(bookings);
        var elapsedTime = Stopwatch.GetElapsedTime(startTime);
        TestContext.Current!.Output.WriteLine("Elapsed time: " + elapsedTime);

        // Assert
        var count = await bookingRepository.CountAsync();
        await Assert.That(count).IsEqualTo(500);
    }
}
