using System.Runtime.CompilerServices;
using Synax.AspNetCore.Errors;
using Synax.EntityFrameworkCore.Abstractions.Patching;

// This assembly's own namespace, not the extended type's.
//
// Validation and PatchValue<T> live in packages that do not reference each other, so this bridge
// has to sit in a third. It stays under Synax.EntityFrameworkCore.AspNetCore so the extension is
// discoverable where it lives rather than appearing on Validation for anyone who merely imported
// Synax.AspNetCore.Errors: a caller opts in with a using for this bridge assembly.
namespace Synax.EntityFrameworkCore.AspNetCore;

/// <summary>
/// Validation rules for merge-patch requests.
/// </summary>
public static class ValidationPatchExtensions
{
    /// <summary>
    /// Applies a field rule only when the property was present in the payload.
    /// <para>
    /// The rule that makes merge-patch validation work: an absent property is not an error — it
    /// means "leave this alone" — so validating it would reject every partial update that happens
    /// not to mention the field. Only supplied values are checked, and they accumulate with every
    /// other rule as usual.
    /// </para>
    /// <para>
    /// The condition is a predicate rather than a <c>bool</c> so that it is never evaluated for an
    /// absent property. That is not just an optimisation: <c>Value</c> is <c>default</c> when
    /// unset, so an eagerly evaluated condition would run against a <c>null</c> string or a zeroed
    /// struct, and rules that dereference it would throw on exactly the requests they were meant
    /// to let through.
    /// </para>
    /// </summary>
    /// <param name="value">The patch member to test.</param>
    /// <param name="predicate">The rule, receiving the supplied value. An explicitly supplied
    /// <c>null</c> (a "clear this field" instruction) reaches it like any other value, so a rule
    /// that should permit clearing must say so.</param>
    /// <param name="code">Machine-readable error code recorded when the rule fails.</param>
    /// <param name="description">Human-readable reason recorded when the rule fails.</param>
    /// <param name="propertyName">The field the error belongs to, for form UIs.</param>
    /// <example>
    /// <code>
    /// var v = new Validation("The profile update is invalid.")
    ///     .RequireIfSet(request.FirstName, name => !string.IsNullOrWhiteSpace(name),
    ///         "USER_INVALID_NAME", "First name must not be empty.", nameof(request.FirstName))
    ///     .RequireIfSet(request.Age, age => age >= 0,
    ///         "USER_INVALID_AGE", "Must not be negative.", nameof(request.Age));
    /// </code>
    /// </example>
    public static Validation RequireIfSet<T>(
        this Validation validation,
        PatchValue<T> value,
        Func<T?, bool> predicate,
        string code,
        string description,
        string? propertyName = null,
        [CallerMemberName] string originMember = "",
        [CallerFilePath] string originFile = "",
        [CallerLineNumber] int originLine = 0)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(predicate);

        if (!value.IsSet) return validation;

        // The caller-info arguments are forwarded rather than left to default, so a failure's
        // ResultOrigin points at the rule the developer wrote instead of at this line.
        return validation.Require(
            predicate(value.Value), code, description, propertyName,
            originMember, originFile, originLine);
    }
}
