using System.Text.Json.Serialization;

namespace Synax.EntityFrameworkCore.Abstractions.Patching;

/// <summary>
/// A three-state optional for PATCH request DTOs: <b>unset</b> (the property was absent from
/// the payload — leave the stored value untouched), <b>set to a value</b>, or <b>set to
/// <c>null</c></b> (explicitly clear the stored value; declare a nullable
/// <typeparamref name="T"/> to allow it).
/// <para>
/// Deserialization does the state tracking: a JSON property that is present — even as
/// <c>null</c> — produces a set instance, an absent property leaves the struct at its
/// default (unset). No serializer registration is needed; the converter is applied through
/// the attribute on this type.
/// </para>
/// <para>
/// Consumed by <c>IRepository.ExecutePatchAsync</c>: every set member is written with a single
/// set-based <c>ExecuteUpdateAsync</c>, matched to the entity property of the same name — no
/// mapper or profile configuration involved.
/// </para>
/// </summary>
[JsonConverter(typeof(PatchValueJsonConverterFactory))]
public readonly struct PatchValue<T>
{
    private PatchValue(T? value)
    {
        Value = value;
        IsSet = true;
    }

    /// <summary>
    /// <c>true</c> when the property was provided (even as <c>null</c>);
    /// <c>false</c> for the default instance (property absent from the payload).
    /// </summary>
    public bool IsSet { get; }

    /// <summary>The provided value; <c>default</c> when <see cref="IsSet"/> is <c>false</c>.</summary>
    public T? Value { get; }

    /// <summary>The absent state — equivalent to <c>default(PatchValue&lt;T&gt;)</c>.</summary>
    public static PatchValue<T> Unset => default;

    /// <summary>Creates a set instance carrying <paramref name="value"/>.</summary>
    public static PatchValue<T> Of(T? value) => new(value);

    /// <summary>A plain value converts to a <b>set</b> instance — object initializers read naturally.</summary>
    public static implicit operator PatchValue<T>(T? value) => new(value);

    public override string ToString() => IsSet ? Value?.ToString() ?? "null" : "unset";
}
