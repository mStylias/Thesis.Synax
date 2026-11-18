using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Modules;
using Synax.Crud.Modules;
using Synax.EntityFrameworkCore.Modules;
using Synax.Mapper.Modules;
using Synax.Mediator.Modules;
using Synax.Modules.UnitTests.TestFixtures;

namespace Synax.Modules.UnitTests;

/// <summary>
/// The half-registered module is the failure the module system exists to catch: surface a module's
/// assemblies contain but its <c>Configure</c> never registered. Each case below leaves out exactly
/// one capability and expects to be told which types went unregistered and which call is missing.
/// </summary>
public class ModuleValidationTests
{
    private static Action Registering(Action<ISynaxModuleBuilder> configure, bool validate = true)
        => () =>
        {
            var services = new ServiceCollection();
            services.AddSynaxModules(options =>
            {
                options.ValidateSurface = validate;
                options.AddModule(new DelegateModule("Sample", configure));
            });
        };

    // -------- Every capability reports what it is missing --------

    [Test]
    public async Task Validate_WhenEndpointsAreNotRegistered_ShouldNameThemAndTheMissingCall()
    {
        // Arrange — everything but AddEndpoints
        var act = Registering(builder =>
        {
            builder.AddHealthChecks();
            builder.AddHandlers();
            builder.AddProfiles();
            builder.AddPersistence<TestDbContext, WidgetContext>();
            builder.AddCrud<WidgetContext>();
        });

        // Act & Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(PingEndpoint));

        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("builder.AddEndpoints()");
    }

    [Test]
    public async Task Validate_WhenHandlersAreNotRegistered_ShouldNameThem()
    {
        // Arrange
        var act = Registering(builder =>
        {
            builder.AddEndpoints();
            builder.AddHealthChecks();
            builder.AddProfiles();
            builder.AddPersistence<TestDbContext, WidgetContext>();
            builder.AddCrud<WidgetContext>();
        });

        // Act & Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(PingQueryHandler));
    }

    [Test]
    public async Task Validate_WhenProfilesAreNotRegistered_ShouldNameThem()
    {
        // Arrange
        var act = Registering(builder =>
        {
            builder.AddEndpoints();
            builder.AddHealthChecks();
            builder.AddHandlers();
            builder.AddPersistence<TestDbContext, WidgetContext>();
            builder.AddCrud<WidgetContext>();
        });

        // Act & Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(WidgetProfile));
    }

    [Test]
    public async Task Validate_WhenEntitiesHaveNoRepositories_ShouldNameThem()
    {
        // Arrange
        var act = Registering(builder =>
        {
            builder.AddEndpoints();
            builder.AddHealthChecks();
            builder.AddHandlers();
            builder.AddProfiles();
            builder.AddCrud<WidgetContext>();
        });

        // Act & Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining(typeof(Widget).FullName!);
    }

    [Test]
    public async Task Validate_WhenHealthChecksAreNotRegistered_ShouldNameThem()
    {
        // Arrange
        var act = Registering(builder =>
        {
            builder.AddEndpoints();
            builder.AddHandlers();
            builder.AddProfiles();
            builder.AddPersistence<TestDbContext, WidgetContext>();
            builder.AddCrud<WidgetContext>();
        });

        // Act & Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(PingHealthCheck));
    }

    [Test]
    public async Task Validate_WhenCrudRequestsHaveNoHandlers_ShouldNameThem()
    {
        // Arrange
        var act = Registering(builder =>
        {
            builder.AddEndpoints();
            builder.AddHealthChecks();
            builder.AddHandlers();
            builder.AddProfiles();
            builder.AddPersistence<TestDbContext, WidgetContext>();
        });

        // Act & Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(GetWidgetRequest));
    }

    [Test]
    public async Task Validate_WhenSurfaceIsMissing_ShouldNameTheModule()
    {
        // Arrange
        var act = Registering(_ => { });

        // Act & Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("Module 'Sample'");
    }

    // -------- Escape hatches --------

    [Test]
    public async Task Validate_WhenTheMissingTypeIsIgnored_ShouldNotThrow()
    {
        // Arrange — endpoints deliberately unregistered, and said so
        var act = Registering(builder =>
        {
            builder.Ignore<PingEndpoint>();
            builder.AddHealthChecks();
            builder.AddHandlers();
            builder.AddProfiles();
            builder.AddPersistence<TestDbContext, WidgetContext>();
            builder.AddCrud<WidgetContext>();
        });

        // Act & Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Validate_WhenDisabled_ShouldNotThrow()
    {
        // Arrange
        var act = Registering(_ => { }, validate: false);

        // Act & Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Validate_WhenAnotherModuleClaimedTheAssembly_ShouldNotThrow()
    {
        // Arrange — two modules over the same assembly, each declaring part of the surface
        var endpointModule = new DelegateModule("Http", builder => builder.AddEndpoints());
        var restModule = new DelegateModule("Core", builder =>
        {
            builder.AddHealthChecks();
            builder.AddHandlers();
            builder.AddProfiles();
            builder.AddPersistence<TestDbContext, WidgetContext>();
            builder.AddCrud<WidgetContext>();
        });

        var act = () =>
        {
            var services = new ServiceCollection();
            services.AddSynaxModules(options =>
            {
                options.AddModule(endpointModule);
                options.AddModule(restModule);
            });
        };

        // Act & Assert — claims are pooled, so neither module is blamed for the other's surface
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Validate_WhenEverythingIsDeclared_ShouldNotThrow()
    {
        // Arrange
        var act = Registering(ModuleRegistrationTests.DeclareEverything);

        // Act & Assert
        await Assert.That(act).ThrowsNothing();
    }
}
