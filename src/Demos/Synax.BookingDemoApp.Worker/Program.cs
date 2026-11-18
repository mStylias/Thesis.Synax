using Synax.BookingDemoApp.Worker;
using Synax.Modules;

// A second host over the same Application/Domain/Infrastructure layers as the API, wired by the
// same one call. Migrations and demo seeding belong to the API host, so start that first.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<MaintenanceOptions>(
    builder.Configuration.GetSection(MaintenanceOptions.SectionName));

builder.Services.AddSynaxModules(builder.Configuration, typeof(BookingWorkerModule).Assembly);

var host = builder.Build();
host.Run();
