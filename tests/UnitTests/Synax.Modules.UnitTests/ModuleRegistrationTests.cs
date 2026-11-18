using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Errors;
using Synax.AspNetCore.HealthChecks;
using Synax.AspNetCore.Modules;
using Synax.Crud.Modules;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Modules;
using Synax.Mapper.Core;
using Synax.Mapper.Modules;
using Synax.Mediator;
using Synax.Mediator.Modules;
using Synax.Modules.UnitTests.TestFixtures;
using Synax.Results;

namespace Synax.Modules.UnitTests;

/// <summary>
/// A module declares its surface once and every library registers its own part of it. These cover
/// what the declaration produces, and that two modules compose without stepping on each other.
/// </summary>
public class ModuleRegistrationTests
{
    /// <summary>Declares every capability the test assembly's fixtures contain.</summary>
    internal static void DeclareEverything(ISynaxModuleBuilder builder)
    {
        builder.AddEndpoints();
        builder.AddHealthChecks();
        builder.AddHandlers();
        builder.AddProfiles();
        builder.AddPersistence<TestDbContext, WidgetContext>();
        builder.AddCrud<WidgetContext>();
    }

    internal static IServiceCollection Register(params ISynaxModule[] modules)
    {
        var services = new ServiceCollection();
        services.AddSynaxModules(options =>
        {
            foreach (var module in modules)
                options.AddModule(module);
        });

        return services;
    }

    private static Type? ImplementationFor(IServiceCollection services, Type serviceType)
        => services.LastOrDefault(descriptor => descriptor.ServiceType == serviceType)?.ImplementationType;

    // -------- One declaration registers every capability --------

    [Test]
    public async Task AddSynaxModules_WhenModuleDeclaresEverything_ShouldRegisterEachCapability()
    {
        // Arrange & Act
        var services = Register(new DelegateModule("Everything", DeclareEverything));

        // Assert — endpoints and health checks
        await Assert.That(services.Any(d =>
            d.ServiceType == typeof(IEndpoint) && d.ImplementationType == typeof(PingEndpoint))).IsTrue();
        await Assert.That(services.Any(d =>
            d.ServiceType == typeof(ISynaxHealthCheck) && d.ImplementationType == typeof(PingHealthCheck))).IsTrue();

        // Assert — handlers
        await Assert.That(ImplementationFor(services, typeof(IRequestHandler<PingQuery, string>)))
            .IsEqualTo(typeof(PingQueryHandler));

        // Assert — persistence, under the standalone tag
        await Assert.That(services.Any(d => d.ServiceType == typeof(IUnitOfWork<WidgetContext>))).IsTrue();
        await Assert.That(services.Any(d => d.ServiceType == typeof(IRepository<Widget, WidgetContext>))).IsTrue();

        // Assert — profiles compile into a usable mapper
        var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var mapped = mapper.Map<WidgetResponse>(new Widget { Name = "cog" });
        await Assert.That(mapped.Name).IsEqualTo("cog");
    }

    [Test]
    public async Task AddSynaxModules_WhenModuleDeclaresCrud_ShouldRegisterAGenericHandler()
    {
        // Arrange & Act
        var services = Register(new DelegateModule("Everything", DeclareEverything));

        // Assert — no handwritten handler exists for GetWidgetRequest, so the generic one serves it
        var handlerType = ImplementationFor(
            services, typeof(IRequestHandler<GetWidgetRequest, Result<WidgetResponse, HttpError>>));

        await Assert.That(handlerType).IsNotNull();
        await Assert.That(handlerType!.Name).StartsWith("CrudGetHandler");
    }

    // -------- CRUD yields to a handwritten handler, in either module order --------

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AddSynaxModules_WhenCrudAndHandlersSplitAcrossModules_ShouldKeepTheHandwrittenHandler(
        bool crudModuleFirst)
    {
        // Arrange — one module owns the handlers, another owns the CRUD registration
        var handlerModule = new DelegateModule("Handlers", builder =>
        {
            builder.AddHandlers();
            builder.AddEndpoints();
            builder.AddHealthChecks();
            builder.AddProfiles();
            builder.AddPersistence<TestDbContext, WidgetContext>();
        });

        var crudModule = new DelegateModule("Crud", builder => builder.AddCrud<WidgetContext>());

        // Act — CRUD registration is deferred past every other module, so order cannot matter
        var services = crudModuleFirst
            ? Register(crudModule, handlerModule)
            : Register(handlerModule, crudModule);

        // Assert
        var handlerType = ImplementationFor(
            services, typeof(IRequestHandler<GetGadgetRequest, Result<GadgetResponse, HttpError>>));

        await Assert.That(handlerType).IsEqualTo(typeof(GetGadgetHandler));
    }

    // -------- Composition across modules --------

    [Test]
    public async Task AddSynaxModules_WhenBehaviorAndHandlersInDifferentModules_ShouldStillWrap()
    {
        // Arrange
        var handlerModule = new DelegateModule("Handlers", DeclareEverything);
        var behaviorModule = new DelegateModule("Behavior", builder =>
            builder.AddBehavior(typeof(RecordingBehavior<,>)));

        // Act
        var services = Register(handlerModule, behaviorModule);
        var recorder = new BehaviorRecorder();
        services.AddSingleton(recorder);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().SendAsync(new PingQuery("hi"));

        // Assert
        await Assert.That(recorder.Calls).Contains(nameof(PingQuery));
    }

    [Test]
    public async Task AddSynaxModules_WhenTwoModulesShareAnAssembly_ShouldNotDuplicateDescriptors()
    {
        // Arrange — both modules scan the same (test) assembly
        var first = new DelegateModule("First", DeclareEverything);
        var second = new DelegateModule("Second", DeclareEverything);

        // Act
        var services = Register(first, second);

        // Assert — the underlying registrations are all idempotent
        await Assert.That(services.Count(d =>
            d.ServiceType == typeof(IEndpoint) && d.ImplementationType == typeof(PingEndpoint))).IsEqualTo(1);
        await Assert.That(services.Count(d => d.ServiceType == typeof(IUnitOfWork<WidgetContext>))).IsEqualTo(1);
        await Assert.That(services.Count(d => d.ServiceType == typeof(IRepository<Widget, WidgetContext>))).IsEqualTo(1);
    }

    [Test]
    public async Task AddSynaxModules_WhenTheSameModuleTypeIsRegisteredTwice_ShouldConfigureItOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        var configureCount = 0;

        void Configure(ISynaxModuleBuilder builder)
        {
            configureCount++;
            DeclareEverything(builder);
        }

        // Act — the second call names a module the registry already has
        services.AddSynaxModules(options => options.AddModule(new DelegateModule("Once", Configure)));
        services.AddSynaxModules(options => options.AddModule(new DelegateModule("Once", Configure)));

        // Assert — instances are distinct objects but share a type, which is the identity used
        await Assert.That(configureCount).IsEqualTo(1);
    }

    // -------- Discovery --------

    [Test]
    public async Task AddSynaxModules_WhenAssemblyHasNoModules_ShouldThrowNamingIt()
    {
        // Arrange
        var services = new ServiceCollection();
        var emptyAssembly = typeof(IMapper).Assembly;

        // Act
        var act = () => services.AddSynaxModules(emptyAssembly);

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("Synax.Mapper");
    }

    [Test]
    public async Task AddSynaxModules_WhenModuleHasNoParameterlessConstructor_ShouldSayHowToFixIt()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddSynaxModules(options => options.AddModule<ConstructorModule>());

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("AddModule(ISynaxModule)");
    }

    private sealed class ConstructorModule(string _) : ISynaxModule
    {
        public void Configure(ISynaxModuleBuilder builder) { }
    }
}

internal sealed class BehaviorRecorder
{
    public List<string> Calls { get; } = [];
}

internal sealed class RecordingBehavior<TRequest, TResponse>(BehaviorRecorder recorder)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        recorder.Calls.Add(typeof(TRequest).Name);
        return next();
    }
}
