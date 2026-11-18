namespace Synax.AspNetCore.Endpoints;

/// <summary>
/// Represents a semantic API version used for endpoint route grouping.
/// </summary>
public readonly record struct ApiVersion(int Major, int Minor = 0)
{
    public override string ToString() => Minor == 0
        ? $"{Major}"
        : $"{Major}.{Minor}";
}
