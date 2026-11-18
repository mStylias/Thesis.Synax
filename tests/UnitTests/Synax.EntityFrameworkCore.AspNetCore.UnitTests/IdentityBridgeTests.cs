using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Identity;
using Synax.Identity;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.AspNetCore.UnitTests;

public class IdentityBridgeTests
{
    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static IServiceScope BuildScope(
        HttpContext? httpContext,
        Action<IdentityBridgeOptions>? configure = null,
        Action<IServiceCollection>? preRegister = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new FakeHttpContextAccessor { HttpContext = httpContext });
        preRegister?.Invoke(services);
        services.AddSynaxEfCoreIdentityBridge<Guid>(configure);
        return services.BuildServiceProvider().CreateScope();
    }

    private static HttpContext ContextWithUser(Guid userId, params Claim[] extraClaims)
        => new DefaultHttpContext
        {
            User = AuthenticatedPrincipal(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                .. extraClaims
            ])
        };

    // -------- Default (owner = user) --------

    [Test]
    public async Task Bridge_WhenAuthenticated_ShouldProvideUserIdToAllThreeProviders()
    {
        var userId = Guid.NewGuid();
        using var scope = BuildScope(ContextWithUser(userId));

        var audit = scope.ServiceProvider.GetRequiredService<IAuditUserProvider>();
        var softDelete = scope.ServiceProvider.GetRequiredService<ISoftDeleteUserProvider>();
        var owner = scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>();

        await Assert.That(audit.GetCurrentUserId()).IsEqualTo(userId);
        await Assert.That(softDelete.GetCurrentUserId()).IsEqualTo(userId);
        await Assert.That(owner.GetCurrentOwnerId()).IsEqualTo(userId);
    }

    [Test]
    public async Task Bridge_WhenNoHttpContext_ShouldProvideNullFromAllThreeProviders()
    {
        using var scope = BuildScope(httpContext: null);

        await Assert.That(scope.ServiceProvider.GetRequiredService<IAuditUserProvider>()
            .GetCurrentUserId()).IsNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ISoftDeleteUserProvider>()
            .GetCurrentUserId()).IsNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>()
            .GetCurrentOwnerId()).IsNull();
    }

    [Test]
    public async Task Bridge_WhenUnauthenticated_ShouldProvideNullFromAllThreeProviders()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())]))
        };
        using var scope = BuildScope(context);

        await Assert.That(scope.ServiceProvider.GetRequiredService<IAuditUserProvider>()
            .GetCurrentUserId()).IsNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>()
            .GetCurrentOwnerId()).IsNull();
    }

    // -------- Owner independent of user --------

    [Test]
    public async Task Bridge_WithOwnerClaim_ShouldSplitOwnerFromUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        using var scope = BuildScope(
            ContextWithUser(userId, new Claim("tenant_id", tenantId.ToString())),
            o => o.OwnerClaim = "tenant_id");

        var audit = scope.ServiceProvider.GetRequiredService<IAuditUserProvider>();
        var owner = scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>();

        await Assert.That(audit.GetCurrentUserId()).IsEqualTo(userId);
        await Assert.That(owner.GetCurrentOwnerId()).IsEqualTo(tenantId);
    }

    [Test]
    public async Task Bridge_WithOwnerClaimMissingFromPrincipal_ShouldProvideNullOwner()
    {
        var userId = Guid.NewGuid();
        using var scope = BuildScope(ContextWithUser(userId), o => o.OwnerClaim = "tenant_id");

        var audit = scope.ServiceProvider.GetRequiredService<IAuditUserProvider>();
        var owner = scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>();

        await Assert.That(audit.GetCurrentUserId()).IsEqualTo(userId);
        await Assert.That(owner.GetCurrentOwnerId()).IsNull();
    }

    [Test]
    public async Task Bridge_WithOwnerSelector_ShouldTakePrecedenceOverOwnerClaim()
    {
        var selectorTenant = Guid.NewGuid();
        var claimTenant = Guid.NewGuid();
        using var scope = BuildScope(
            ContextWithUser(Guid.NewGuid(), new Claim("tenant_id", claimTenant.ToString())),
            o =>
            {
                o.OwnerClaim = "tenant_id";
                o.OwnerSelector = _ => selectorTenant.ToString();
            });

        var owner = scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>();

        await Assert.That(owner.GetCurrentOwnerId()).IsEqualTo(selectorTenant);
    }

    // -------- User context scope overrides --------

    [Test]
    public async Task Bridge_WithBecomeOwnerScope_ShouldProvideOwnerButNoUser()
    {
        var tenantId = Guid.NewGuid();
        using var scope = BuildScope(httpContext: null);
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeOwner(tenantId))
        {
            await Assert.That(scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>()
                .GetCurrentOwnerId()).IsEqualTo(tenantId);
            await Assert.That(scope.ServiceProvider.GetRequiredService<IAuditUserProvider>()
                .GetCurrentUserId()).IsNull();
        }
    }

    [Test]
    public async Task Bridge_WithBecomeUserAndOwnerScope_ShouldProvideBothIndependently()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        using var scope = BuildScope(httpContext: null);
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeUser(userId, tenantId))
        {
            await Assert.That(scope.ServiceProvider.GetRequiredService<IAuditUserProvider>()
                .GetCurrentUserId()).IsEqualTo(userId);
            await Assert.That(scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>()
                .GetCurrentOwnerId()).IsEqualTo(tenantId);
        }
    }

    [Test]
    public async Task Bridge_WithBecomeSystemScope_ShouldBeatOwnerClaimConfiguration()
    {
        var tenantId = Guid.NewGuid();
        using var scope = BuildScope(
            ContextWithUser(Guid.NewGuid(), new Claim("tenant_id", tenantId.ToString())),
            o => o.OwnerClaim = "tenant_id");
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        var owner = scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>();
        await Assert.That(owner.GetCurrentOwnerId()).IsEqualTo(tenantId);

        using (userContext.BecomeSystem())
        {
            await Assert.That(owner.GetCurrentOwnerId()).IsNull();
        }
    }

    [Test]
    public async Task Bridge_WithBecomeAllOwnersScope_ShouldReportCrossOwnerAndKeepTheUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        using var scope = BuildScope(
            ContextWithUser(userId, new Claim("tenant_id", tenantId.ToString())),
            o => o.OwnerClaim = "tenant_id");
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        var owner = scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>();
        await Assert.That(owner.IsCrossOwner()).IsFalse();

        using (userContext.BecomeAllOwners())
        {
            // Reads widen through the query filter's cross-owner term; there is no single owner
            // to stamp, but the acting user is still the one auditing rows.
            await Assert.That(owner.IsCrossOwner()).IsTrue();
            await Assert.That(owner.GetCurrentOwnerId()).IsNull();
            await Assert.That(scope.ServiceProvider.GetRequiredService<IAuditUserProvider>()
                .GetCurrentUserId()).IsEqualTo(userId);
        }

        await Assert.That(owner.IsCrossOwner()).IsFalse();
        await Assert.That(owner.GetCurrentOwnerId()).IsEqualTo(tenantId);
    }

    [Test]
    public async Task Bridge_WithMismatchedOwnerKeyType_ShouldThrowDescriptiveError()
    {
        using var scope = BuildScope(httpContext: null);
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();
        var owner = scope.ServiceProvider.GetRequiredService<IOwnerProvider<Guid>>();

        using (userContext.BecomeOwner(42))
        {
            var exception = Assert.Throws<InvalidOperationException>(() => owner.GetCurrentOwnerId());

            await Assert.That(exception.Message).Contains("Int32");
            await Assert.That(exception.Message).Contains("Guid");
        }
    }

    // -------- Registration --------

    [Test]
    public async Task Bridge_WhenAuditProviderAlreadyRegistered_ShouldNotOverwriteIt()
    {
        var userId = Guid.NewGuid();
        using var scope = BuildScope(
            ContextWithUser(userId),
            preRegister: services => services.AddScoped<IAuditUserProvider, FixedAuditUserProvider>());

        var audit = scope.ServiceProvider.GetRequiredService<IAuditUserProvider>();

        await Assert.That(audit).IsTypeOf<FixedAuditUserProvider>();
        await Assert.That(audit.GetCurrentUserId()).IsEqualTo("system");
    }

    private sealed class FixedAuditUserProvider : IAuditUserProvider
    {
        public object? GetCurrentUserId() => "system";
    }
}
