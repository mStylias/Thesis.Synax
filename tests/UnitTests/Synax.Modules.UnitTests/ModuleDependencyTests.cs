using Microsoft.Extensions.DependencyInjection;

namespace Synax.Modules.UnitTests;

/// <summary>
/// Layer modules are siblings that reference nothing of each other, so only a host assembly can
/// name them all. <c>DependsOn</c> is what keeps each host to one <c>AddSynaxModules</c> call, and
/// what lets a second host reuse the same layers without referencing the first.
/// </summary>
public class ModuleDependencyTests
{
    private static IServiceCollection Register(params Type[] moduleTypes)
    {
        var services = new ServiceCollection();
        services.AddSynaxModules(options =>
        {
            // Surface validation is covered elsewhere; these modules declare no surface.
            options.ValidateSurface = false;

            foreach (var moduleType in moduleTypes)
                options.AddModule(moduleType);
        });

        return services;
    }

    private static int CountOf<T>(IServiceCollection services)
        => services.Count(descriptor => descriptor.ServiceType == typeof(T));

    [Test]
    public async Task DependsOn_ShouldRegisterDependenciesTransitively()
    {
        // Arrange & Act — the host names one module; the chain below it comes along
        var services = Register(typeof(HostModule));

        // Assert
        await Assert.That(CountOf<MiddleMarker>(services)).IsEqualTo(1);
        await Assert.That(CountOf<LeafMarker>(services)).IsEqualTo(1);
    }

    [Test]
    public async Task DependsOn_WhenTwoHostsShareADependency_ShouldConfigureItOnce()
    {
        // Arrange & Act — an API host and a worker host in one process, both over the same layer
        var services = Register(typeof(HostModule), typeof(SecondHostModule));

        // Assert
        await Assert.That(CountOf<LeafMarker>(services)).IsEqualTo(1);
    }

    [Test]
    public async Task DependsOn_WhenModulesDependOnEachOther_ShouldNotLoop()
    {
        // Arrange
        var act = () => Register(typeof(PingModule));

        // Act & Assert — the already-registered guard makes cycles a non-event
        await Assert.That(act).ThrowsNothing();
        await Assert.That(CountOf<PongMarker>(Register(typeof(PingModule)))).IsEqualTo(1);
    }

    [Test]
    public async Task DependsOn_WhenTheDependencyIsAlsoNamedDirectly_ShouldConfigureItOnce()
    {
        // Arrange & Act
        var services = Register(typeof(LeafModule), typeof(HostModule));

        // Assert
        await Assert.That(CountOf<LeafMarker>(services)).IsEqualTo(1);
    }

    // -------- Fixtures --------

    private sealed record LeafMarker;

    private sealed record MiddleMarker;

    private sealed record PongMarker;

    private sealed class LeafModule : ISynaxModule
    {
        public void Configure(ISynaxModuleBuilder builder)
            => builder.AddServices(services => services.AddSingleton(new LeafMarker()));
    }

    private sealed class MiddleModule : ISynaxModule
    {
        public void Configure(ISynaxModuleBuilder builder)
        {
            builder.DependsOn<LeafModule>();
            builder.AddServices(services => services.AddSingleton(new MiddleMarker()));
        }
    }

    private sealed class HostModule : ISynaxModule
    {
        public void Configure(ISynaxModuleBuilder builder) => builder.DependsOn<MiddleModule>();
    }

    private sealed class SecondHostModule : ISynaxModule
    {
        public void Configure(ISynaxModuleBuilder builder) => builder.DependsOn<LeafModule>();
    }

    private sealed class PingModule : ISynaxModule
    {
        public void Configure(ISynaxModuleBuilder builder) => builder.DependsOn<PongModule>();
    }

    private sealed class PongModule : ISynaxModule
    {
        public void Configure(ISynaxModuleBuilder builder)
        {
            builder.DependsOn<PingModule>();
            builder.AddServices(services => services.AddSingleton(new PongMarker()));
        }
    }
}
