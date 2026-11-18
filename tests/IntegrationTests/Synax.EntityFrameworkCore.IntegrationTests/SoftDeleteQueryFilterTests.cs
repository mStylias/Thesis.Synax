using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class SoftDeleteQueryFilterTests(BookingDatabaseFixture fixture)
{
    // --------- Query filter tests ---------- //

    [Test]
    public async Task QueryAsync_WhenEntityIsSoftDeleted_ShouldExcludeFromResults()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "Active", IsDeleted = false },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repository.AddAsync(
            new SoftDeletableBooking { Name = "Deleted", IsDeleted = true },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var results = await repository
            .Query()
            .ToListAsync();

        // Assert
        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Name).IsEqualTo("Active");

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task QueryAsync_WhenIgnoreQueryFilters_ShouldIncludeSoftDeleted()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "Active", IsDeleted = false },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repository.AddAsync(
            new SoftDeletableBooking { Name = "Deleted", IsDeleted = true },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var results = await repository
            .Query(q => q.IgnoreQueryFilters())
            .ToListAsync();

        // Assert
        await Assert.That(results).Count().IsEqualTo(2);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task QueryAsync_WhenEntityIsNotSoftDeletable_ShouldReturnAll()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var bookingRepository = unitOfWork.GetRepository<Booking>();

        await bookingRepository.AddAsync(
            new Booking { Name = "Regular 1", Description = "Desc" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await bookingRepository.AddAsync(
            new Booking { Name = "Regular 2", Description = "Desc" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var results = await bookingRepository
            .Query()
            .ToListAsync();

        // Assert
        await Assert.That(results).Count().IsEqualTo(2);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task CountAsync_WhenSoftDeletedEntitiesExist_ShouldExcludeThem()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "Active 1", IsDeleted = false },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repository.AddAsync(
            new SoftDeletableBooking { Name = "Active 2", IsDeleted = false },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repository.AddAsync(
            new SoftDeletableBooking { Name = "Deleted", IsDeleted = true },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var count = await repository.CountAsync();

        // Assert
        await Assert.That(count).IsEqualTo(2);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task AnyAsync_WhenOnlySoftDeletedExist_ShouldReturnFalse()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "OnlyDeleted", IsDeleted = true },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        var any = await repository.Query(q => q.Where(b => b.Name == "OnlyDeleted")).AnyAsync();

        // Assert
        await Assert.That(any).IsFalse();

        await unitOfWork.RollbackTransactionAsync();
    }

    // --------- Interceptor tests ---------- //

    [Test]
    public async Task RemoveAndSaveChangesAsync_WhenSoftDeletable_ShouldSetIsDeletedInsteadOfRemoving()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "ToSoftDelete" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var entity = await repository
            .Query(q => q.Where(b => b.Name == "ToSoftDelete"), asNoTracking: false)
            .SingleAsync();

        // Act
        repository.Remove(entity);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var normalCount = await repository.CountAsync();
        await Assert.That(normalCount).IsEqualTo(0);

        var allEntities = await repository
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Name == "ToSoftDelete"), asNoTracking: false)
            .SingleAsync();
        await Assert.That(allEntities.IsDeleted).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ExecuteDeleteAsync_WhenSoftDeletable_ShouldConvertToSoftDelete()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "BulkSoftDelete" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        await repository.ExecuteDeleteAsync(q => q.Where(b => b.Name == "BulkSoftDelete"));

        // Assert
        var normalCount = await repository.CountAsync();
        await Assert.That(normalCount).IsEqualTo(0);

        var unfiltered = await repository
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Name == "BulkSoftDelete"))
            .SingleAsync();
        await Assert.That(unfiltered.IsDeleted).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }

    // --------- Hard delete bypass tests ---------- //

    [Test]
    public async Task HardDeleteAndSaveChangesAsync_WhenCalled_ShouldPermanentlyDelete()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "ToHardDelete" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var entity = await repository
            .Query(q => q.Where(b => b.Name == "ToHardDelete"), asNoTracking: false)
            .SingleAsync();

        // Act
        repository.HardDelete(entity);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var exists = await repository
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Name == "ToHardDelete"))
            .AnyAsync();
        await Assert.That(exists).IsFalse();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ExecuteHardDeleteAsync_WhenCalled_ShouldPermanentlyDelete()
    {
        // Arrange
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<SoftDeletableBooking>();

        await repository.AddAsync(
            new SoftDeletableBooking { Name = "BulkHardDelete" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        await repository.ExecuteHardDeleteAsync(q => q.Where(b => b.Name == "BulkHardDelete"));

        // Assert
        var exists = await repository
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Name == "BulkHardDelete"))
            .AnyAsync();
        await Assert.That(exists).IsFalse();

        await unitOfWork.RollbackTransactionAsync();
    }

    // --------- User tracking tests ---------- //

    [Test]
    public async Task RemoveAndSaveChangesAsync_WhenAuditedEntity_ShouldSetDeletedByIdAndDeletedAt()
    {
        // Arrange
        fixture.UserProvider.CurrentUserId = 42;
        await using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);

        dbContext.TestUsers.Add(new TestUser { Id = 42, Name = "TestUser42" });
        await dbContext.SaveChangesAsync();

        var repository = unitOfWork.GetRepository<AuditedSoftDeletableBooking>();
        await repository.AddAsync(
            new AuditedSoftDeletableBooking { Name = "AuditedDelete" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var entity = await repository
            .Query(q => q.Where(b => b.Name == "AuditedDelete"), asNoTracking: false)
            .SingleAsync();

        // Act
        repository.Remove(entity);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var deleted = await repository
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Name == "AuditedDelete"), asNoTracking: false)
            .SingleAsync();
        await Assert.That(deleted.IsDeleted).IsTrue();
        await Assert.That(deleted.DeletedAt).IsNotNull();
        await Assert.That(deleted.DeletedById).IsEqualTo(42);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ExecuteDeleteAsync_WhenAuditedEntity_ShouldSetDeletedByIdAndDeletedAt()
    {
        // TODO: Fix [Test Failure] 23503: insert or update on table "AuditedSoftDeletableBookings" violates foreign key constraint "FK_AuditedSoftDeletableBookings_TestUsers_DeletedById"

        // Arrange
        fixture.UserProvider.CurrentUserId = 99;
        await using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);

        dbContext.TestUsers.Add(new TestUser { Id = 99, Name = "TestUser99" });
        await dbContext.SaveChangesAsync();

        var repository = unitOfWork.GetRepository<AuditedSoftDeletableBooking>();
        await repository.AddAsync(
            new AuditedSoftDeletableBooking { Id = 1, Name = "BulkAuditedDelete" },
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        await repository.ExecuteDeleteAsync(q => q.Where(b => b.Name == "BulkAuditedDelete"));

        // Assert
        var deleted = await repository
            .Query(q => q.IgnoreQueryFilters().Where(b => b.Name == "BulkAuditedDelete"))
            .SingleAsync();
        await Assert.That(deleted.IsDeleted).IsTrue();
        await Assert.That(deleted.DeletedAt).IsNotNull();
        await Assert.That(deleted.DeletedById).IsEqualTo(99);

        await unitOfWork.RollbackTransactionAsync();
    }
}
