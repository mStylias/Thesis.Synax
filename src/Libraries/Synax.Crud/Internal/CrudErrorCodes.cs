using System.Text;

namespace Synax.Crud.Internal;

/// <summary>
/// Convention-derived error codes for the generic handlers: the entity CLR name in upper
/// snake case ("MenuItem" → "MENU_ITEM") prefixes the operation code, matching the shape of
/// hand-authored codes (<c>MENU_ITEM_NOT_FOUND</c>). Deterministic and stable as long as the
/// entity type name doesn't change.
/// </summary>
internal static class CrudErrorCodes<TEntity>
{
    internal static readonly string Prefix = ToUpperSnake(typeof(TEntity).Name);

    internal static readonly string NotFound = $"{Prefix}_NOT_FOUND";
    internal static readonly string CreateFailed = $"{Prefix}_CREATE_FAILED";
    internal static readonly string UpdateFailed = $"{Prefix}_UPDATE_FAILED";
    internal static readonly string DeleteFailed = $"{Prefix}_DELETE_FAILED";
    internal static readonly string FetchFailed = $"{Prefix}_FETCH_FAILED";

    /// <summary>Human-readable entity name for error descriptions ("MenuItem" → "menu item").</summary>
    internal static readonly string DisplayName = ToDisplayName(typeof(TEntity).Name);

    private static string ToUpperSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
                sb.Append('_');
            sb.Append(char.ToUpperInvariant(name[i]));
        }

        return sb.ToString();
    }

    private static string ToDisplayName(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
                sb.Append(' ');
            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }
}
