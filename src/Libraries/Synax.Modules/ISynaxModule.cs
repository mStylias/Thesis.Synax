namespace Synax.Modules;

/// <summary>
/// One module's entire Synax surface, declared in one place.
/// <para>
/// Without this, every capability a module needs has its own central registration call
/// (<c>AddSynaxMediator</c>, <c>AddSynaxEndpoints</c>, <c>AddSynaxEfCore</c>, <c>AddSynaxMapper</c>, ...),
/// so the composition root becomes a hand-maintained checklist and a module that wires its handlers
/// but never gets its endpoints scanned fails silently with 404s at runtime. A module states what it
/// has once, and <c>AddSynaxModules</c> both performs the registration and verifies that nothing in
/// the module's assemblies was left behind.
/// </para>
/// <example>
/// <code>
/// public sealed class BookingModule : ISynaxModule
/// {
///     public void Configure(ISynaxModuleBuilder builder)
///     {
///         builder.IncludeAssemblyOf&lt;Booking&gt;();
///         builder.AddEndpoints();
///         builder.AddHandlers();
///         builder.AddProfiles();
///         builder.AddPersistence&lt;BookingDbContext, BookingContext&gt;();
///     }
/// }
/// </code>
/// </example>
/// </summary>
/// <remarks>
/// Implementations are discovered by assembly scan and instantiated with their public parameterless
/// constructor, so a module must not take dependencies through its constructor — read configuration
/// from <see cref="ISynaxModuleBuilder.Configuration"/> instead.
/// </remarks>
public interface ISynaxModule
{
    /// <summary>
    /// The module's name, used in validation messages. Defaults to the implementing type's name.
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// Declares the module's surface. Capability methods (<c>AddEndpoints</c>, <c>AddHandlers</c>, ...)
    /// are extension methods contributed by the individual Synax libraries the module references.
    /// </summary>
    void Configure(ISynaxModuleBuilder builder);
}
