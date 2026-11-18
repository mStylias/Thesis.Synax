using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Identity;
using Synax.Identity;

namespace Synax.AspNetCore.UnitTests;

public class UserContextScopeTests
{
    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static IServiceProvider BuildProvider<TKey>(HttpContext? httpContext)
        where TKey : notnull
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new FakeHttpContextAccessor { HttpContext = httpContext });
        services.AddSynaxCurrentUser<TKey>();
        return services.BuildServiceProvider();
    }

    private static HttpContext ContextWithUser(Guid userId)
        => new DefaultHttpContext
        {
            User = AuthenticatedPrincipal(new Claim(ClaimTypes.NameIdentifier, userId.ToString()))
        };

    // -------- Override replaces the principal --------

    [Test]
    public async Task BecomeUser_ShouldOverrideAmbientPrincipalAndRevertOnDispose()
    {
        var ambientUserId = Guid.NewGuid();
        var overrideUserId = Guid.NewGuid();

        using var scope = BuildProvider<Guid>(ContextWithUser(ambientUserId)).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeUser(overrideUserId))
        {
            await Assert.That(currentUser.IsAuthenticated).IsTrue();
            await Assert.That(currentUser.TryGetId(out var typedId)).IsTrue();
            await Assert.That(typedId).IsEqualTo(overrideUserId);
            await Assert.That(currentUser.Id).IsEqualTo(overrideUserId.ToString());
            await Assert.That(currentUser.Principal).IsNull();
        }

        await Assert.That(currentUser.TryGetId(out var ambientId)).IsTrue();
        await Assert.That(ambientId).IsEqualTo(ambientUserId);
        await Assert.That(currentUser.Principal).IsNotNull();
    }

    [Test]
    public async Task BecomeSystem_InsideAuthenticatedRequest_ShouldBeatTheRealPrincipal()
    {
        using var scope = BuildProvider<Guid>(ContextWithUser(Guid.NewGuid())).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeSystem())
        {
            await Assert.That(currentUser.IsAuthenticated).IsFalse();
            await Assert.That(currentUser.TryGetId(out _)).IsFalse();
            await Assert.That(currentUser.Id).IsNull();
        }
    }

    // -------- Nesting --------

    [Test]
    public async Task NestedScopes_InnermostWinsEntirely_AndDisposeRestoresOuter()
    {
        var userA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();

        using var scope = BuildProvider<Guid>(httpContext: null).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeUser(userA))
        {
            using (userContext.BecomeOwner(ownerB))
            {
                // The inner owner-only scope fully replaces the outer scope: no user.
                await Assert.That(currentUser.TryGetId(out _)).IsFalse();
            }

            await Assert.That(currentUser.TryGetId(out var restoredId)).IsTrue();
            await Assert.That(restoredId).IsEqualTo(userA);
        }
    }

    [Test]
    public async Task BecomeAllOwners_ShouldLeaveTheAmbientPrincipalInPlace()
    {
        var ambientUserId = Guid.NewGuid();

        using var scope = BuildProvider<Guid>(ContextWithUser(ambientUserId)).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeAllOwners())
        {
            // Owner scoping is lifted, but the admin doing it is still the acting user — that is
            // what audit columns must record.
            await Assert.That(currentUser.IsAuthenticated).IsTrue();
            await Assert.That(currentUser.TryGetId(out var typedId)).IsTrue();
            await Assert.That(typedId).IsEqualTo(ambientUserId);
            await Assert.That(currentUser.Principal).IsNotNull();
        }

        await Assert.That(userContext.Current).IsNull();
    }

    [Test]
    public async Task BecomeAllOwners_NestedInsideBecomeUser_ShouldKeepTheOuterUser()
    {
        var userA = Guid.NewGuid();

        using var scope = BuildProvider<Guid>(httpContext: null).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeUser(userA))
        using (userContext.BecomeAllOwners())
        {
            await Assert.That(currentUser.TryGetId(out var observed)).IsTrue();
            await Assert.That(observed).IsEqualTo(userA);
            await Assert.That(userContext.Current!.AllOwners).IsTrue();
        }
    }

    [Test]
    public async Task BecomeOwner_NestedInsideBecomeAllOwners_ShouldNarrowBackDown()
    {
        var ownerB = Guid.NewGuid();

        using var scope = BuildProvider<Guid>(httpContext: null).CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeAllOwners())
        {
            using (userContext.BecomeOwner(ownerB))
            {
                await Assert.That(userContext.Current!.AllOwners).IsFalse();
                await Assert.That(userContext.Current!.OwnerId).IsEqualTo((object)ownerB);
            }

            await Assert.That(userContext.Current!.AllOwners).IsTrue();
        }
    }

    // -------- Async flow isolation --------

    [Test]
    public async Task ParallelScopes_ShouldNotCrossTalkAcrossAsyncFlows()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        using var scope = BuildProvider<Guid>(httpContext: null).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        async Task<Guid> ObserveAsAsync(Guid userId)
        {
            using (userContext.BecomeUser(userId))
            {
                await Task.Yield();
                return currentUser.TryGetId(out var observed) ? observed : Guid.Empty;
            }
        }

        var observed = await Task.WhenAll(ObserveAsAsync(userA), ObserveAsAsync(userB));

        await Assert.That(observed[0]).IsEqualTo(userA);
        await Assert.That(observed[1]).IsEqualTo(userB);
    }

    // -------- Key-type mismatch --------

    [Test]
    public async Task BecomeUser_WithMismatchedKeyType_ShouldThrowDescriptiveError()
    {
        using var scope = BuildProvider<Guid>(httpContext: null).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContextScope>();

        using (userContext.BecomeUser(42))
        {
            var exception = Assert.Throws<InvalidOperationException>(() => currentUser.TryGetId(out _));

            await Assert.That(exception.Message).Contains("Int32");
            await Assert.That(exception.Message).Contains("Guid");
        }
    }
}
