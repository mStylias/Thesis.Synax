using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.ExceptionHandling;
using Synax.AspNetCore.HealthChecks;
using Synax.AspNetCore.Identity;
using Synax.Identity;

namespace Synax.AspNetCore;

public static class DependencyInjectionExtensions
{
    // Statuses serialize by name ("Healthy") rather than by ordinal.
    private static readonly JsonSerializerOptions HealthCheckJsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // ---------- Endpoint Discovery ---------- //

    /// <summary>
    /// Scans the given assemblies for <see cref="IEndpoint"/> (and <see cref="IVersionedEndpoint"/>)
    /// implementations and registers them with the DI container.
    /// Endpoint types that are already registered are skipped, so scanning the same
    /// assembly more than once does not produce duplicate routes.
    /// </summary>
    public static IServiceCollection AddSynaxEndpoints(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var endpointTypes = GetLoadableTypes(assembly)
                .Where(t => t is { IsAbstract: false, IsClass: true, ContainsGenericParameters: false }
                            && typeof(IEndpoint).IsAssignableFrom(t));

            foreach (var type in endpointTypes)
            {
                if (services.Any(d => d.ServiceType == typeof(IEndpoint) && d.ImplementationType == type))
                    continue;

                services.Add(new ServiceDescriptor(typeof(IEndpoint), type, ServiceLifetime.Transient));
            }
        }

        return services;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    /// <summary>
    /// Maps all registered <see cref="IEndpoint"/> instances as minimal API endpoints.
    /// Non-versioned endpoints are grouped by <see cref="IEndpoint.Tag"/> under <c>/{tag}/</c>.
    /// Versioned endpoints are grouped under <c>/api/v{version}/{tag}/</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapSynaxEndpoints(this IEndpointRouteBuilder app)
    {
        // Present only when the module system registered the endpoints, which is what lets it fail
        // startup if this call is missing.
        app.ServiceProvider.GetService<EndpointMappingProbe>()?.MarkMapped();

        using var scope = app.ServiceProvider.CreateScope();
        var endpoints = scope.ServiceProvider.GetServices<IEndpoint>();

        var versioned = new List<IVersionedEndpoint>();
        var nonVersioned = new List<IEndpoint>();

        foreach (var endpoint in endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Tag))
                throw new InvalidOperationException(
                    $"Endpoint '{endpoint.GetType().FullName}' has a null or empty Tag. " +
                    "Every endpoint must provide a non-empty Tag for route grouping.");

            if (endpoint is IVersionedEndpoint ve)
                versioned.Add(ve);
            else
                nonVersioned.Add(endpoint);
        }

        // Tags are grouped case-insensitively: prefixes are lowercased, so "Bookings" and
        // "bookings" would otherwise map the same route twice (ambiguous match at runtime).
        foreach (var group in nonVersioned.GroupBy(e => e.Tag, StringComparer.OrdinalIgnoreCase))
        {
            var routeGroup = app.MapGroup(group.Key.ToLowerInvariant())
                .WithTags(group.Key);

            foreach (var endpoint in group)
                endpoint.AddEndpoint(routeGroup);
        }

        foreach (var group in versioned.GroupBy(
                     e => (e.Version, Tag: e.Tag.ToLowerInvariant())))
        {
            var (version, lowerTag) = group.Key;
            var prefix = $"api/v{version}/{lowerTag}";
            var routeGroup = app.MapGroup(prefix)
                .WithTags(group.First().Tag)
                .WithGroupName($"v{version}");

            foreach (var endpoint in group)
                endpoint.AddEndpoint(routeGroup);
        }

        return app;
    }

    // ---------- Current User ---------- //

    /// <summary>
    /// Registers <see cref="ICurrentUser"/>, derived from the ambient <see cref="HttpContext"/> principal.
    /// Auth-agnostic: works with whatever authentication the app configured (bearer, cookies, ...).
    /// </summary>
    public static IServiceCollection AddSynaxCurrentUser(
        this IServiceCollection services, Action<CurrentUserOptions>? configure = null)
    {
        services.AddHttpContextAccessor();
        services.Configure(configure ?? (_ => { }));
        services.AddSynaxUserContext();
        services.TryAddScoped<ICurrentUser, HttpContextCurrentUser>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ICurrentUser"/> and <see cref="ICurrentUser{TKey}"/> with the user id
    /// parsed to <typeparamref name="TKey"/> (string, Guid, int, long supported out of the box).
    /// </summary>
    public static IServiceCollection AddSynaxCurrentUser<TKey>(
        this IServiceCollection services, Action<CurrentUserOptions>? configure = null)
        where TKey : notnull
    {
        services.AddSynaxCurrentUser(configure);
        services.TryAddScoped<ICurrentUser<TKey>, HttpContextCurrentUser<TKey>>();
        return services;
    }

    // ---------- Exception Handler ---------- //

    /// <summary>
    /// Registers the Synax exception handler with default options (ProblemDetails enabled).
    /// Call <c>app.UseExceptionHandler()</c> to activate it in the pipeline.
    /// </summary>
    public static IServiceCollection AddSynaxExceptionHandler(this IServiceCollection services)
        => services.AddSynaxExceptionHandler(_ => { });

    /// <summary>
    /// Registers the Synax exception handler with custom options.
    /// Call <c>app.UseExceptionHandler()</c> to activate it in the pipeline.
    /// <para>
    /// Repeated calls accumulate their mappings onto one handler instead of registering the
    /// handler again, so several modules can each contribute the exceptions they know about.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSynaxExceptionHandler(
        this IServiceCollection services, Action<SynaxExceptionHandlerOptions> configure)
    {
        services.AddProblemDetails();

        if (services.All(d => d.ImplementationType != typeof(SynaxExceptionHandler)))
            services.AddExceptionHandler<SynaxExceptionHandler>();

        services.Configure(configure);
        return services;
    }

    // ----------  Health Checks ---------- //

    /// <summary>
    /// Registers Synax health checks with the DI container.
    /// Check types that are already registered are skipped, so repeated calls
    /// do not run the same check twice.
    /// </summary>
    public static IServiceCollection AddSynaxHealthChecks(
        this IServiceCollection services, Action<SynaxHealthCheckOptions> configure)
    {
        var options = new SynaxHealthCheckOptions();
        configure(options);

        foreach (var checkType in options.HealthCheckTypes)
        {
            if (services.Any(d => d.ServiceType == typeof(ISynaxHealthCheck) && d.ImplementationType == checkType))
                continue;

            services.Add(new ServiceDescriptor(typeof(ISynaxHealthCheck), checkType, ServiceLifetime.Transient));
        }

        return services;
    }

    /// <summary>
    /// Maps a health check endpoint at the given <paramref name="pattern"/>.
    /// Returns a JSON response with individual check results and an overall status.
    /// <para>
    /// Pass <paramref name="cacheDuration"/> to cache the computed response: probes arriving
    /// within the window get the cached payload without re-running the checks, and when it
    /// expires only one caller recomputes while concurrent callers wait for the fresh result.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapSynaxHealthChecks(
        this IEndpointRouteBuilder app, string pattern = "/health", TimeSpan? cacheDuration = null)
    {
        CachedHealthResponse? cached = null;
        var refreshLock = new SemaphoreSlim(1, 1);

        app.MapGet(pattern, async (HttpContext context) =>
        {
            if (cacheDuration is null)
            {
                var (response, statusCode) = await RunHealthChecksAsync(context);
                return TypedResults.Json(response, HealthCheckJsonOptions, statusCode: statusCode);
            }

            var timeProvider = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

            var snapshot = Volatile.Read(ref cached);
            if (snapshot is not null && timeProvider.GetElapsedTime(snapshot.Timestamp) < cacheDuration)
                return TypedResults.Json(snapshot.Response, HealthCheckJsonOptions, statusCode: snapshot.StatusCode);

            await refreshLock.WaitAsync(context.RequestAborted);
            try
            {
                snapshot = Volatile.Read(ref cached);
                if (snapshot is not null && timeProvider.GetElapsedTime(snapshot.Timestamp) < cacheDuration)
                    return TypedResults.Json(snapshot.Response, HealthCheckJsonOptions, statusCode: snapshot.StatusCode);

                var (response, statusCode) = await RunHealthChecksAsync(context);
                Volatile.Write(ref cached,
                    new CachedHealthResponse(response, statusCode, timeProvider.GetTimestamp()));
                return TypedResults.Json(response, HealthCheckJsonOptions, statusCode: statusCode);
            }
            finally
            {
                refreshLock.Release();
            }
        }).WithTags("Health").ExcludeFromDescription().AllowAnonymous();

        return app;
    }

    private static async Task<(SynaxHealthCheckResponse Response, int StatusCode)> RunHealthChecksAsync(
        HttpContext context)
    {
        var checks = context.RequestServices.GetServices<ISynaxHealthCheck>();
        var totalStopwatch = Stopwatch.StartNew();

        // Checks run concurrently so total latency is the slowest check, not the sum.
        var results = await Task.WhenAll(checks.Select(async check =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await check.CheckAsync(context.RequestAborted);
                sw.Stop();
                return new SynaxHealthCheckResult
                {
                    Name = result.Name,
                    Status = result.Status,
                    Description = result.Description,
                    Duration = sw.Elapsed
                };
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new SynaxHealthCheckResult
                {
                    Name = check.Name,
                    Status = SynaxHealthStatus.Unhealthy,
                    Description = $"Check threw exception: {ex.Message}",
                    Duration = sw.Elapsed
                };
            }
        }));

        totalStopwatch.Stop();

        var overallStatus = results.Length == 0
            ? SynaxHealthStatus.Healthy
            : results.Any(r => r.Status == SynaxHealthStatus.Unhealthy)
                ? SynaxHealthStatus.Unhealthy
                : results.Any(r => r.Status == SynaxHealthStatus.Degraded)
                    ? SynaxHealthStatus.Degraded
                    : SynaxHealthStatus.Healthy;

        var response = new SynaxHealthCheckResponse
        {
            OverallStatus = overallStatus,
            Checks = results,
            TotalDuration = totalStopwatch.Elapsed
        };

        var statusCode = overallStatus == SynaxHealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

        return (response, statusCode);
    }

    private sealed record CachedHealthResponse(
        SynaxHealthCheckResponse Response, int StatusCode, long Timestamp);
}
