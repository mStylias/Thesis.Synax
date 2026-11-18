namespace Synax.Modules;

/// <summary>
/// A kind of surface a module can declare — endpoints, handlers, mapper profiles, entities.
/// Each Synax library defines the capabilities it owns; the module core knows only the metadata
/// needed to register a claim and to write a useful validation message.
/// </summary>
/// <param name="Name">Stable identifier, e.g. <c>"Endpoints"</c>.</param>
/// <param name="Surface">
/// Singular noun for the types this capability covers, e.g. <c>"endpoint"</c>. Pluralized in
/// messages by appending <c>s</c>.
/// </param>
/// <param name="RegistrationCall">
/// The builder call that registers it, e.g. <c>"AddEndpoints()"</c>. Quoted verbatim when
/// validation reports the capability as unclaimed, so it must read as callable code.
/// </param>
public sealed record SynaxModuleCapability(string Name, string Surface, string RegistrationCall)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
