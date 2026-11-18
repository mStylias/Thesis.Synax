namespace Synax.BookingDemoApp.Application.Auth;

/// <summary>
/// Settings for the demo's self-issued JWTs (bound from the <c>Jwt</c> configuration section).
/// The login handler signs tokens with these values and the host's JwtBearer middleware
/// validates against the same ones. Demo only — a real app would use a proper identity provider.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Claim carrying the tenant; the identity bridge reads it via <c>OwnerClaim</c>.</summary>
    public const string TenantClaim = "tenant_id";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 120;
}
