using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;
using Synax.BookingDemoApp.Application.Auth;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.BookingDemoApp.Infrastructure.Seeding;
using Synax.EntityFrameworkCore.AspNetCore;
using Synax.EntityFrameworkCore.OData;
using Synax.Mediator;

namespace Synax.BookingDemoApp;

/// <summary>
/// Host concerns that belong to the application rather than to any module: JSON, OpenAPI,
/// authentication, telemetry, and the app-wide OData guardrails. The module surface —
/// endpoints, handlers, persistence — is declared by <see cref="BookingModule"/>.
/// </summary>
public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Enums cross the wire as their names ("Confirmed", "Terrace"), matching how the
        // entities store them (HasConversion<string>).
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()));

        services
            .AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header
                };
                document.Security ??= new List<OpenApiSecurityRequirement>();
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
                return Task.CompletedTask;
            }))
            // Guardrails for the OData search endpoints; the helpers fall back to defaults
            // without this, so it's here to make the knobs visible. App-wide rather than
            // per-module, which is why it stays out of BookingModule.
            .AddSynaxOData(options =>
            {
                options.MaxTop = 100;

                // $compute: clients may derive values from the exposed shape and then filter,
                // order or select them. Restricted to the numeric members worth computing over,
                // so a client cannot build expressions across the whole DTO.
                options.EnableCompute = true;
                options.AllowedComputeProperties.Add("maxCapacity");
                options.AllowedComputeProperties.Add("seats");
                options.AllowedComputeProperties.Add("number");

                // Server-driven paging: every search response caps at 25 rows and hands back an
                // @odata.nextLink. Shared by both search endpoints because the options are.
                options.PageSize = 25;

                // $search over restaurants. Declaring the members is what enables it — a $search
                // against TableResponse (which exposes no free-text member; its Location is an
                // enum projected to a string) is a 400 naming the type, never a match-everything.
                options.Searchable<RestaurantResponse>(
                    restaurant => restaurant.Name,
                    restaurant => restaurant.Address,
                    restaurant => restaurant.PhoneNumber);

                // $apply: aggregation over the exposed shape, answered by a GROUP BY. Capped at the
                // methods a reporting client actually needs — no countdistinct, which is the one
                // that cannot use an index — and at a chain shallow enough to stay one statement.
                options.EnableApply = true;
                options.AllowedAggregations.Add("sum");
                options.AllowedAggregations.Add("min");
                options.AllowedAggregations.Add("max");
                options.AllowedAggregations.Add("average");
                options.AllowedAggregations.Add("$count");
                options.MaxAggregationDepth = 3;

                // The shapes the search endpoints expose, declared once so app.MapSynaxODataMetadata()
                // can publish them as one service at /odata — which is what lets Excel, Power BI or
                // any generic OData client discover the surface before querying it. Naming the sets
                // here rather than deriving them keeps the published contract stable when a DTO is
                // renamed. EnableAnnotations is deliberately left off: the demo's endpoints return
                // typed DTOs, and turning annotations on reshapes every row into a dictionary to make
                // room for @odata.id — a trade an app makes on purpose, not by default.
                options.Expose<RestaurantResponse>("restaurants");
                options.Expose<TableResponse>("tables");
            });

        // The infrastructure module already registered the bridge with defaults, because its
        // interceptors cannot work without one. This adds the part only an authenticating host
        // knows: which claim carries the tenant. Configure accumulates, so the two compose.
        // Synax consumes identity; JwtBearer below establishes it.
        services.AddSynaxEfCoreIdentityBridge<Guid>(options =>
            options.OwnerClaim = JwtOptions.TenantClaim);

        // Migrates and seeds the demo world. A host concern, not a module one — the worker host
        // deliberately does not seed.
        services.AddHostedService<DataSeeder>();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep claims exactly as issued ("sub", "role", "tenant_id") instead of the
                // legacy SOAP-era remapping — ICurrentUser falls back to "sub" natively.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization();

        if (environment.IsDevelopment())
        {
            // Mediator telemetry: spans per send/publish (failed Results tagged with
            // synax.error_code / origin / messages) and count+duration metrics.
            services.AddOpenTelemetry()
                .WithTracing(tracing => tracing
                    .AddSource(MediatorTelemetry.SourceName)
                    .AddConsoleExporter())
                .WithMetrics(metrics => metrics
                    .AddMeter(MediatorTelemetry.SourceName));
        }

        return services;
    }
}
