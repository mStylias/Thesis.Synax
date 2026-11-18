using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synax.Identity;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;

namespace Synax.BookingDemoApp.Infrastructure.Seeding;

/// <summary>
/// Migrates the database and seeds the demo world on startup. Seeding runs inside
/// <see cref="IUserContextScope"/> blocks: global data as <c>BecomeSystem()</c> (no user, no
/// owner — optional-owner rows stay shared), tenant data as <c>BecomeUser(userId, tenantId)</c>
/// so the ownership and auditing interceptors stamp <c>OwnerId</c>/<c>CreatedById</c> exactly
/// like they would for a real authenticated request.
/// </summary>
public class DataSeeder(
    IServiceScopeFactory scopeFactory,
    IUserContextScope userContext,
    ILogger<DataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        if (await unitOfWork.GetRepository<AppUser>().AnyAsync(cancellationToken))
        {
            logger.LogInformation("Demo data already present — skipping seed.");
            return;
        }

        await SeedSharedDataAsync(unitOfWork, cancellationToken);
        await SeedTenantAAsync(unitOfWork, cancellationToken);
        await SeedTenantBAsync(unitOfWork, cancellationToken);

        logger.LogInformation(
            "Demo data seeded. Log in via POST /auth/login with e.g. {Alice} (tenant A owner) or {Diana} (tenant B owner).",
            DemoData.AliceEmail, DemoData.DianaEmail);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedSharedDataAsync(IUnitOfWork<BookingContext> unitOfWork, CancellationToken ct)
    {
        // System context: no ambient user or owner, so optional-owner cuisines stay shared (null).
        using var _ = userContext.BecomeSystem();

        var userRepo = unitOfWork.GetRepository<AppUser>();
        var cuisineRepo = unitOfWork.GetRepository<Cuisine>();

        AppUser[] users =
        [
            new() { Id = DemoData.AliceId, Name = "Alice Romano", Email = DemoData.AliceEmail, Role = UserRole.Owner, TenantId = DemoData.TenantAId },
            new() { Id = DemoData.BobId, Name = "Bob Kessler", Email = DemoData.BobEmail, Role = UserRole.Staff, TenantId = DemoData.TenantAId },
            new() { Id = DemoData.CarlaId, Name = "Carla Mendes", Email = DemoData.CarlaEmail, Role = UserRole.Customer, TenantId = DemoData.TenantAId },
            new() { Id = DemoData.DianaId, Name = "Diana Sato", Email = DemoData.DianaEmail, Role = UserRole.Owner, TenantId = DemoData.TenantBId },
            new() { Id = DemoData.EvanId, Name = "Evan Wright", Email = DemoData.EvanEmail, Role = UserRole.Customer, TenantId = DemoData.TenantBId }
        ];

        foreach (var user in users)
        {
            await userRepo.AddAsync(user, ct);
        }

        Cuisine[] cuisines =
        [
            new() { Id = DemoData.ItalianCuisineId, Name = "Italian", Description = "Pasta, pizza, and everything in between." },
            new() { Id = DemoData.JapaneseCuisineId, Name = "Japanese", Description = "Sushi, ramen, izakaya." },
            new() { Id = DemoData.GreekCuisineId, Name = "Greek", Description = "Mezze and grilled classics." }
        ];

        foreach (var cuisine in cuisines)
        {
            await cuisineRepo.AddAsync(cuisine, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task SeedTenantAAsync(IUnitOfWork<BookingContext> unitOfWork, CancellationToken ct)
    {
        // Act as Alice inside tenant A — interceptors stamp OwnerId + CreatedById automatically.
        using var _ = userContext.BecomeUser(DemoData.AliceId, DemoData.TenantAId);

        await unitOfWork.GetRepository<Cuisine>().AddAsync(new Cuisine
        {
            Id = DemoData.CypriotCuisineId,
            Name = "Cypriot",
            Description = "Tenant A's custom cuisine — invisible to other tenants."
        }, ct);

        await unitOfWork.GetRepository<Restaurant>().AddAsync(new Restaurant
        {
            Id = DemoData.LaTavolaId,
            Name = "La Tavola",
            Address = "12 Via Roma, Nicosia",
            PhoneNumber = "+357 22 000001",
            CuisineId = DemoData.ItalianCuisineId,
            MaxCapacity = 60
        }, ct);

        var tableRepo = unitOfWork.GetRepository<Table>();
        for (var number = 1; number <= 6; number++)
        {
            await tableRepo.AddAsync(new Table
            {
                Id = Guid.CreateVersion7(),
                RestaurantId = DemoData.LaTavolaId,
                Number = number,
                Seats = number <= 4 ? 4 : 8,
                Location = number switch
                {
                    <= 2 => TableLocation.Indoor,
                    <= 4 => TableLocation.Terrace,
                    _ => TableLocation.PrivateRoom
                }
            }, ct);
        }

        var menuRepo = unitOfWork.GetRepository<MenuItem>();
        (string Name, decimal Price, MenuCategory Category)[] menu =
        [
            ("Bruschetta", 6.50m, MenuCategory.Starter),
            ("Tagliatelle al Ragu", 14.90m, MenuCategory.Main),
            ("Margherita", 11.00m, MenuCategory.Main),
            ("Tiramisu", 7.00m, MenuCategory.Dessert),
            ("House Red (glass)", 5.50m, MenuCategory.Beverage)
        ];

        foreach (var (name, price, category) in menu)
        {
            await menuRepo.AddAsync(new MenuItem
            {
                Id = Guid.CreateVersion7(),
                RestaurantId = DemoData.LaTavolaId,
                Name = name,
                Price = price,
                Category = category
            }, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task SeedTenantBAsync(IUnitOfWork<BookingContext> unitOfWork, CancellationToken ct)
    {
        using var _ = userContext.BecomeUser(DemoData.DianaId, DemoData.TenantBId);

        await unitOfWork.GetRepository<Restaurant>().AddAsync(new Restaurant
        {
            Id = DemoData.SakuraHouseId,
            Name = "Sakura House",
            Address = "3 Blossom Lane, Limassol",
            PhoneNumber = "+357 25 000002",
            CuisineId = DemoData.JapaneseCuisineId,
            MaxCapacity = 40
        }, ct);

        var tableRepo = unitOfWork.GetRepository<Table>();
        for (var number = 1; number <= 4; number++)
        {
            await tableRepo.AddAsync(new Table
            {
                Id = Guid.CreateVersion7(),
                RestaurantId = DemoData.SakuraHouseId,
                Number = number,
                Seats = 4,
                Location = number <= 2 ? TableLocation.Indoor : TableLocation.Bar
            }, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
