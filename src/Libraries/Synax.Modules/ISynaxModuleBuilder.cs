using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Synax.Modules;

/// <summary>
/// The surface a module declares against. Synax libraries extend this interface with the
/// capabilities they own — <c>AddHandlers()</c> from Synax.Mediator, <c>AddEndpoints()</c> from
/// Synax.AspNetCore, and so on — so a module only sees the capabilities of the libraries it
/// actually references.
/// </summary>
public interface ISynaxModuleBuilder
{
    /// <summary>The type implementing <see cref="ISynaxModule"/>.</summary>
    Type ModuleType { get; }

    /// <summary>The module's name, used in validation messages.</summary>
    string ModuleName { get; }

    /// <summary>The application's service collection, for registrations Synax knows nothing about.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The host's configuration, when <c>AddSynaxModules</c> was given one. Modules take no
    /// constructor dependencies, so this is how a module reads its own settings.
    /// </summary>
    IConfiguration? Configuration { get; }

    /// <summary>
    /// The assemblies that make up this module: the one declaring the module type, plus any added
    /// with <see cref="IncludeAssembly"/>. Capability methods scan these by default and surface
    /// validation is performed over them.
    /// </summary>
    /// <remarks>
    /// Capability registration is deferred until every module has been configured, so
    /// <see cref="IncludeAssembly"/> may be called after the capability methods it affects.
    /// </remarks>
    IReadOnlyList<Assembly> Assemblies { get; }

    /// <summary>Whether surface validation is enabled for this registration.</summary>
    bool ValidationEnabled { get; }

    /// <summary>
    /// Declares that this module needs <typeparamref name="TModule"/>, which is registered along
    /// with it. This is what keeps a host to a single <c>AddSynaxModules</c> call when the modules
    /// follow a layered split: only the host assembly references every layer, so the host's module
    /// is the one place that can name the shared ones.
    /// <para>
    /// A second host (worker, CLI) declares its own module over the same dependencies and gets the
    /// same shared registrations without referencing the first host.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Cycles are harmless: a module already registered is not configured again. Dependencies are
    /// configured after the module that declares them, which changes nothing — capability
    /// registration is deferred until every module has been configured.
    /// </remarks>
    ISynaxModuleBuilder DependsOn<TModule>() where TModule : ISynaxModule;

    /// <inheritdoc cref="DependsOn{TModule}"/>
    ISynaxModuleBuilder DependsOn(Type moduleType);

    /// <summary>
    /// Adds an assembly to the module. A module spanning several assemblies (a domain and an
    /// application layer, say) names each of them here.
    /// </summary>
    ISynaxModuleBuilder IncludeAssembly(Assembly assembly);

    /// <summary>Adds the assembly declaring <typeparamref name="T"/> to the module.</summary>
    ISynaxModuleBuilder IncludeAssemblyOf<T>();

    /// <summary>
    /// Excludes types from surface validation — for a surface type that is deliberately registered
    /// elsewhere, or deliberately not registered at all.
    /// </summary>
    ISynaxModuleBuilder Ignore(params Type[] types);

    /// <inheritdoc cref="Ignore(Type[])"/>
    ISynaxModuleBuilder Ignore<T>();

    /// <summary>Runs <paramref name="configure"/> against the service collection immediately.</summary>
    ISynaxModuleBuilder AddServices(Action<IServiceCollection> configure);

    /// <summary>
    /// Queues a registration to run once every module has been configured. Capability methods use
    /// this so they see the module's complete assembly list regardless of call order.
    /// </summary>
    ISynaxModuleBuilder Defer(Action<IServiceCollection> registration);

    /// <summary>
    /// Queues a registration to run after every <see cref="Defer"/> callback of every module.
    /// This is for registrations that must observe everything else first — generic CRUD handlers,
    /// which yield to a handwritten handler for the same request, are the motivating case.
    /// </summary>
    ISynaxModuleBuilder DeferLast(Action<IServiceCollection> registration);

    /// <summary>
    /// Records that <paramref name="capability"/> covers the whole of <paramref name="assembly"/>,
    /// so surface validation stops looking for unregistered types of that capability there.
    /// </summary>
    ISynaxModuleBuilder Claim(SynaxModuleCapability capability, Assembly assembly);

    /// <summary>
    /// Records that <paramref name="capability"/> covers exactly <paramref name="types"/>. Use this
    /// where registration is type-by-type rather than assembly-wide (entities matched by namespace,
    /// for example), so anything the registration missed is still reported.
    /// </summary>
    ISynaxModuleBuilder Claim(SynaxModuleCapability capability, IEnumerable<Type> types);
}
