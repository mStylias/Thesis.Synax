namespace Synax.AspNetCore.Identity;

/// <summary>
/// Parses raw claim string values into typed keys. Supports <c>string</c>, <c>Guid</c>,
/// <c>int</c>, and <c>long</c>. Failures throw descriptive errors at first use rather than
/// at startup — a misconfigured claim selector should explain itself, not fail silently.
/// For other key types, register a custom <see cref="ICurrentUser{TKey}"/> implementation.
/// </summary>
public static class ClaimKeyParser
{
    /// <summary>
    /// Parses <paramref name="rawValue"/> into <typeparamref name="TKey"/>.
    /// </summary>
    /// <param name="rawValue">The claim value to parse.</param>
    /// <param name="claimDescription">
    /// Where the value came from (e.g. "user id claim"), used in error messages.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The key type is unsupported, or the value does not parse — both indicate a
    /// configuration bug (wrong claim selected or wrong key type chosen).
    /// </exception>
    public static TKey Parse<TKey>(string rawValue, string claimDescription) where TKey : notnull
    {
        if (typeof(TKey) == typeof(string))
            return (TKey)(object)rawValue;

        object? parsed = null;

        if (typeof(TKey) == typeof(Guid))
        {
            if (Guid.TryParse(rawValue, out var guid)) parsed = guid;
        }
        else if (typeof(TKey) == typeof(int))
        {
            if (int.TryParse(rawValue, out var i)) parsed = i;
        }
        else if (typeof(TKey) == typeof(long))
        {
            if (long.TryParse(rawValue, out var l)) parsed = l;
        }
        else
        {
            throw new InvalidOperationException(
                $"Key type '{typeof(TKey).Name}' is not supported by the built-in claim key parser " +
                $"(supported: string, Guid, int, long). Implement and register your own " +
                $"ICurrentUser<{typeof(TKey).Name}> to use this key type.");
        }

        if (parsed is null)
            throw new InvalidOperationException(
                $"The {claimDescription} value '{rawValue}' is not a valid {typeof(TKey).Name}. " +
                "Check that the configured claim selector picks the right claim for this key type.");

        return (TKey)parsed;
    }
}
