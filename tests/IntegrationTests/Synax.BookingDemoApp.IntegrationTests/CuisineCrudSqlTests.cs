using Synax.BookingDemoApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Application.Features.Cuisines;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Patching;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves the generic Synax.Crud handlers (registered for <c>Cuisine</c> in
/// <c>Features/Cuisines/CuisineCrud.cs</c>) against real SQL: 404 by ownership scoping, the
/// merge-patch semantics (explicit null clears, unset stays untouched), and hard delete —
/// all dispatched through the real mediator pipeline.
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class CuisineCrudSqlTests(BookingFixture fixture)
{
    [Test]
    public async Task GetCuisine_ReturnsProjectedResponse_ForTenantOwnedCuisine()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Cuisine(cuisineId, ownerId: tenant));

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new GetCuisineRequest { Id = cuisineId });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Name).IsEqualTo($"Cuisine {cuisineId:N}");
    }

    [Test]
    public async Task GetCuisine_OfAnotherTenant_IsNotFound_ViaOwnershipFilter()
    {
        var owner = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsTenant(owner);
        await fixture.SeedAsync(TestData.Cuisine(cuisineId, ownerId: owner));

        fixture.ActAsTenant(otherTenant);
        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new GetCuisineRequest { Id = cuisineId });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo(ErrorCodes.Cuisines.NotFound);
    }

    [Test]
    public async Task PatchCuisine_WritesOnlyProvidedMembers()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Cuisine(cuisineId, ownerId: tenant));

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new PatchCuisineRequest
        {
            Id = cuisineId,
            Name = "Patched"
        });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Name).IsEqualTo("Patched");

        var after = await GetCuisineAsync(cuisineId);
        await Assert.That(after.Name).IsEqualTo("Patched");
        // Unset member kept its stored value (TestData.Cuisine seeds a description).
        await Assert.That(after.Description).IsEqualTo("Test cuisine");
    }

    [Test]
    public async Task PatchCuisine_ExplicitNullDescription_ClearsIt()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Cuisine(cuisineId, ownerId: tenant));

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new PatchCuisineRequest
        {
            Id = cuisineId,
            Description = PatchValue<string?>.Of(null)
        });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Description).IsNull();

        var after = await GetCuisineAsync(cuisineId);
        await Assert.That(after.Description).IsNull();
        // Name is untouched.
        await Assert.That(after.Name).IsEqualTo($"Cuisine {cuisineId:N}");
    }

    [Test]
    public async Task PatchCuisine_UnknownId_ReturnsNotFound()
    {
        fixture.ActAsTenant(Guid.NewGuid());

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new PatchCuisineRequest
        {
            Id = Guid.NewGuid(),
            Name = "Nope"
        });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo(ErrorCodes.Cuisines.NotFound);
    }

    [Test]
    public async Task DeleteCuisine_RemovesRow_ThenSecondDeleteIsNotFound()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        // Standalone cuisine (no Restaurant references it), so the hard delete hits no FK.
        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Cuisine(cuisineId, ownerId: tenant));

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var firstResult = await sender.SendAsync(new DeleteCuisineRequest { Id = cuisineId });
        await Assert.That(firstResult.IsSuccess).IsTrue();

        var gone = await CuisineExistsAsync(cuisineId);
        await Assert.That(gone).IsFalse();

        var secondResult = await sender.SendAsync(new DeleteCuisineRequest { Id = cuisineId });
        await Assert.That(secondResult.IsError).IsTrue();
        await Assert.That(secondResult.Error!.ErrorCode).IsEqualTo(ErrorCodes.Cuisines.NotFound);
    }

    private async Task<Cuisine> GetCuisineAsync(Guid id)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        return await unitOfWork.GetRepository<Cuisine>()
            .Query(q => q.Where(c => c.Id == id))
            .FirstAsync();
    }

    private async Task<bool> CuisineExistsAsync(Guid id)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        return await unitOfWork.GetRepository<Cuisine>()
            .Query(q => q.Where(c => c.Id == id))
            .AnyAsync();
    }
}
