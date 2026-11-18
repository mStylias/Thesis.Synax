using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.HealthChecks;
using Synax.AspNetCore.UnitTests.TestFixtures;

namespace Synax.AspNetCore.UnitTests;

public class HealthCheckTests
{
    // -------- Options registration --------

    [Test]
    public async Task AddSynaxHealthChecks_ShouldRegisterChecksInDI()
    {
        var services = new ServiceCollection();

        services.AddSynaxHealthChecks(opts =>
        {
            opts.AddCheck<HealthyCheck>();
            opts.AddCheck<UnhealthyCheck>();
        });

        var provider = services.BuildServiceProvider();
        var checks = provider.GetServices<ISynaxHealthCheck>().ToList();

        await Assert.That(checks.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AddSynaxHealthChecks_WhenSameCheckRegisteredTwice_ShouldRegisterOnce()
    {
        var services = new ServiceCollection();

        services.AddSynaxHealthChecks(opts => opts.AddCheck<HealthyCheck>());
        services.AddSynaxHealthChecks(opts => opts.AddCheck<HealthyCheck>());

        var provider = services.BuildServiceProvider();
        var checks = provider.GetServices<ISynaxHealthCheck>().ToList();

        await Assert.That(checks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MapSynaxHealthChecks_ShouldSerializeStatusesAsStrings()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSynaxHealthChecks(opts => opts.AddCheck<HealthyCheck>());
        await using var app = builder.Build();

        app.MapSynaxHealthChecks();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        await Assert.That(context.Response.StatusCode).IsEqualTo(200);
        await Assert.That(body).Contains("\"Healthy\"");
    }

    // -------- Response caching --------

    [Test]
    public async Task MapSynaxHealthChecks_WithCacheDuration_ShouldNotRerunChecksWithinTtl()
    {
        var counting = new CountingCheck();
        var time = new ManualTimeProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ISynaxHealthCheck>(counting);
        builder.Services.AddSingleton<TimeProvider>(time);
        await using var app = builder.Build();

        app.MapSynaxHealthChecks("/health", TimeSpan.FromSeconds(5));

        var first = await InvokeHealthEndpoint(app);
        var second = await InvokeHealthEndpoint(app);

        await Assert.That(counting.Count).IsEqualTo(1);
        await Assert.That(first.Body).Contains("\"Healthy\"");
        await Assert.That(second.Body).Contains("\"Healthy\"");

        time.Advance(TimeSpan.FromSeconds(6));
        await InvokeHealthEndpoint(app);

        await Assert.That(counting.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MapSynaxHealthChecks_WithoutCacheDuration_ShouldRunChecksOnEveryCall()
    {
        var counting = new CountingCheck();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ISynaxHealthCheck>(counting);
        await using var app = builder.Build();

        app.MapSynaxHealthChecks();

        await InvokeHealthEndpoint(app);
        await InvokeHealthEndpoint(app);

        await Assert.That(counting.Count).IsEqualTo(2);
    }

    private static async Task<(int StatusCode, string Body)> InvokeHealthEndpoint(WebApplication app)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    [Test]
    public async Task AddCheck_WhenInvalidType_ShouldThrow()
    {
        var options = new SynaxHealthCheckOptions();

        await Assert.That(() => options.AddCheck(typeof(string)))
            .Throws<ArgumentException>();
    }

    // -------- Health check result factories --------

    [Test]
    public async Task Healthy_ShouldReturnHealthyStatus()
    {
        var result = SynaxHealthCheckResult.Healthy("Test", "OK");

        await Assert.That(result.Status).IsEqualTo(SynaxHealthStatus.Healthy);
        await Assert.That(result.Name).IsEqualTo("Test");
        await Assert.That(result.Description).IsEqualTo("OK");
    }

    [Test]
    public async Task Degraded_ShouldReturnDegradedStatus()
    {
        var result = SynaxHealthCheckResult.Degraded("Test");

        await Assert.That(result.Status).IsEqualTo(SynaxHealthStatus.Degraded);
    }

    [Test]
    public async Task Unhealthy_ShouldReturnUnhealthyStatus()
    {
        var result = SynaxHealthCheckResult.Unhealthy("Test", "Down");

        await Assert.That(result.Status).IsEqualTo(SynaxHealthStatus.Unhealthy);
        await Assert.That(result.Description).IsEqualTo("Down");
    }

    // -------- Health check execution --------

    [Test]
    public async Task AllHealthy_ShouldReturnHealthyOverall()
    {
        var checks = new ISynaxHealthCheck[] { new HealthyCheck() };
        var response = await RunChecks(checks);

        await Assert.That(response.OverallStatus).IsEqualTo(SynaxHealthStatus.Healthy);
    }

    [Test]
    public async Task OneUnhealthy_ShouldReturnUnhealthyOverall()
    {
        var checks = new ISynaxHealthCheck[] { new HealthyCheck(), new UnhealthyCheck() };
        var response = await RunChecks(checks);

        await Assert.That(response.OverallStatus).IsEqualTo(SynaxHealthStatus.Unhealthy);
    }

    [Test]
    public async Task OneDegraded_ShouldReturnDegradedOverall()
    {
        var checks = new ISynaxHealthCheck[] { new HealthyCheck(), new DegradedCheck() };
        var response = await RunChecks(checks);

        await Assert.That(response.OverallStatus).IsEqualTo(SynaxHealthStatus.Degraded);
    }

    [Test]
    public async Task NoChecks_ShouldReturnHealthyOverall()
    {
        var response = await RunChecks([]);

        await Assert.That(response.OverallStatus).IsEqualTo(SynaxHealthStatus.Healthy);
        await Assert.That(response.Checks.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ThrowingCheck_ShouldReturnUnhealthy()
    {
        var checks = new ISynaxHealthCheck[] { new ThrowingCheck() };
        var response = await RunChecks(checks);

        await Assert.That(response.OverallStatus).IsEqualTo(SynaxHealthStatus.Unhealthy);
        await Assert.That(response.Checks[0].Description).Contains("exception");
    }

    [Test]
    public async Task Checks_ShouldTrackDuration()
    {
        var checks = new ISynaxHealthCheck[] { new HealthyCheck() };
        var response = await RunChecks(checks);

        await Assert.That(response.Checks[0].Duration).IsGreaterThanOrEqualTo(TimeSpan.Zero);
        await Assert.That(response.TotalDuration).IsGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // -------- Helper --------

    private static async Task<SynaxHealthCheckResponse> RunChecks(ISynaxHealthCheck[] checks)
    {
        var results = new List<SynaxHealthCheckResult>();
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var check in checks)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await check.CheckAsync();
                sw.Stop();
                results.Add(new SynaxHealthCheckResult
                {
                    Name = result.Name,
                    Status = result.Status,
                    Description = result.Description,
                    Duration = sw.Elapsed
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new SynaxHealthCheckResult
                {
                    Name = check.Name,
                    Status = SynaxHealthStatus.Unhealthy,
                    Description = $"Check threw exception: {ex.Message}",
                    Duration = sw.Elapsed
                });
            }
        }

        totalSw.Stop();

        var overallStatus = results.Count == 0
            ? SynaxHealthStatus.Healthy
            : results.Any(r => r.Status == SynaxHealthStatus.Unhealthy)
                ? SynaxHealthStatus.Unhealthy
                : results.Any(r => r.Status == SynaxHealthStatus.Degraded)
                    ? SynaxHealthStatus.Degraded
                    : SynaxHealthStatus.Healthy;

        return new SynaxHealthCheckResponse
        {
            OverallStatus = overallStatus,
            Checks = results,
            TotalDuration = totalSw.Elapsed
        };
    }
}
