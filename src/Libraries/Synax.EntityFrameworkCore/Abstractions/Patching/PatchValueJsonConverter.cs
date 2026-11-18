using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synax.EntityFrameworkCore.Abstractions.Patching;

/// <summary>
/// Creates the <see cref="PatchValue{T}"/> converters. Applied through the
/// <c>[JsonConverter]</c> attribute on the struct — no serializer registration needed.
/// </summary>
public sealed class PatchValueJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(PatchValue<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(PatchValueJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

/// <summary>
/// Reads a JSON value into a <b>set</b> <see cref="PatchValue{T}"/>. Being invoked at all means
/// the property was present in the payload — absence never reaches the converter and leaves the
/// struct default (unset). An explicit <c>null</c> becomes a set instance carrying <c>null</c>
/// when <typeparamref name="T"/> can hold it; for non-nullable value types it fails
/// deserialization like any other type mismatch.
/// </summary>
internal sealed class PatchValueJsonConverter<T> : JsonConverter<PatchValue<T>>
{
    // Null tokens must reach Read() so an explicit null is recorded as "set to null".
    public override bool HandleNull => true;

    public override PatchValue<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => PatchValue<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, PatchValue<T> value, JsonSerializerOptions options)
    {
        if (value.IsSet)
            JsonSerializer.Serialize(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}
