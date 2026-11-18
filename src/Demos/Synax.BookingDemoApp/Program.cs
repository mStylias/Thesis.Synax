using Scalar.AspNetCore;
using Synax.AspNetCore;
using Synax.BookingDemoApp;
using Synax.Mapper.Core;
using Synax.EntityFrameworkCore.OData;
using Synax.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPresentation(builder.Configuration, builder.Environment)
    // One call for the whole API: BookingApiModule declares the HTTP surface and depends on the
    // application and infrastructure modules, which come along with it. Startup fails if any of
    // the three leaves Synax surface in its assemblies unregistered.
    .AddSynaxModules(builder.Configuration, typeof(BookingApiModule).Assembly);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Fail fast on any destination member nothing maps to.
    app.Services.GetRequiredService<MapperConfiguration>().AssertConfigurationIsValid();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapSynaxEndpoints();
app.MapSynaxHealthChecks();                                   // GET /health — always fresh
app.MapSynaxHealthChecks("/ready", TimeSpan.FromSeconds(5));  // GET /ready — cached, single-flight

// GET /odata (service document) + GET /odata/$metadata (CSDL), describing the shapes AddPresentation
// exposed. App-level and called once, like the two above — and outside a module for the same reason.
app.MapSynaxODataMetadata();

app.Run();
