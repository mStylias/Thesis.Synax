using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Identity;

namespace Synax.AspNetCore.UnitTests;

public class CurrentUserTests
{
    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static ClaimsPrincipal UnauthenticatedPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims));

    private static IServiceProvider BuildProvider<TKey>(
        HttpContext? httpContext, Action<CurrentUserOptions>? configure = null)
        where TKey : notnull
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new FakeHttpContextAccessor { HttpContext = httpContext });
        services.AddSynaxCurrentUser<TKey>(configure);
        return services.BuildServiceProvider();
    }

    private static HttpContext ContextWithUser(ClaimsPrincipal principal)
        => new DefaultHttpContext { User = principal };

    // -------- Authenticated --------

    [Test]
    public async Task CurrentUser_WhenAuthenticatedWithNameIdentifier_ShouldExposeIdAndParseKey()
    {
        var userId = Guid.NewGuid();
        var context = ContextWithUser(AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())));

        using var scope = BuildProvider<Guid>(context).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();

        await Assert.That(currentUser.IsAuthenticated).IsTrue();
        await Assert.That(currentUser.Id).IsEqualTo(userId.ToString());
        await Assert.That(currentUser.TryGetId(out var typedId)).IsTrue();
        await Assert.That(typedId).IsEqualTo(userId);
    }

    [Test]
    public async Task CurrentUser_WhenOnlySubClaim_ShouldFallBackToSub()
    {
        var userId = Guid.NewGuid();
        var context = ContextWithUser(AuthenticatedPrincipal(new Claim("sub", userId.ToString())));

        using var scope = BuildProvider<Guid>(context).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();

        await Assert.That(currentUser.Id).IsEqualTo(userId.ToString());
        await Assert.That(currentUser.TryGetId(out var typedId)).IsTrue();
        await Assert.That(typedId).IsEqualTo(userId);
    }

    // -------- No user --------

    [Test]
    public async Task CurrentUser_WhenUnauthenticated_ShouldReportNoUser()
    {
        var context = ContextWithUser(UnauthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())));

        using var scope = BuildProvider<Guid>(context).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();

        await Assert.That(currentUser.IsAuthenticated).IsFalse();
        await Assert.That(currentUser.Id).IsNull();
        await Assert.That(currentUser.TryGetId(out _)).IsFalse();
    }

    [Test]
    public async Task CurrentUser_WhenNoHttpContext_ShouldReportNoUserWithoutThrowing()
    {
        using var scope = BuildProvider<Guid>(httpContext: null).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();

        await Assert.That(currentUser.IsAuthenticated).IsFalse();
        await Assert.That(currentUser.Id).IsNull();
        await Assert.That(currentUser.Principal).IsNull();
        await Assert.That(currentUser.TryGetId(out _)).IsFalse();
    }

    // -------- Configuration --------

    [Test]
    public async Task CurrentUser_WithCustomUserIdSelector_ShouldUseItAndParseInt()
    {
        var context = ContextWithUser(AuthenticatedPrincipal(new Claim("employee_id", "42")));

        using var scope = BuildProvider<int>(
            context, o => o.UserIdSelector = p => p.FindFirst("employee_id")?.Value).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<int>>();

        await Assert.That(currentUser.Id).IsEqualTo("42");
        await Assert.That(currentUser.TryGetId(out var typedId)).IsTrue();
        await Assert.That(typedId).IsEqualTo(42);
    }

    [Test]
    public async Task CurrentUser_WhenClaimValueIsNotAGuid_ShouldThrowDescriptiveError()
    {
        var context = ContextWithUser(AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "not-a-guid")));

        using var scope = BuildProvider<Guid>(context).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser<Guid>>();

        var exception = Assert.Throws<InvalidOperationException>(() => currentUser.TryGetId(out _));
        await Assert.That(exception.Message).Contains("not-a-guid");
    }

    [Test]
    public async Task ClaimKeyParser_WhenUnsupportedKeyType_ShouldThrowDescriptiveError()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ClaimKeyParser.Parse<DateTime>("2020-01-01", "user id claim"));

        await Assert.That(exception.Message).Contains("string, Guid, int, long");
    }

    // -------- Non-generic --------

    [Test]
    public async Task CurrentUser_NonGeneric_ShouldBeRegisteredAndExposeRawId()
    {
        var context = ContextWithUser(AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "raw-id")));

        using var scope = BuildProvider<string>(context).CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        await Assert.That(currentUser.IsAuthenticated).IsTrue();
        await Assert.That(currentUser.Id).IsEqualTo("raw-id");
        await Assert.That(currentUser.Principal).IsNotNull();
    }
}
