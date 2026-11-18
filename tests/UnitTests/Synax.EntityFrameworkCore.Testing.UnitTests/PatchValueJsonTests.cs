using System.Text.Json;
using Synax.EntityFrameworkCore.Abstractions.Patching;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// The deserialization contract that makes <see cref="PatchValue{T}"/> work as a PATCH
/// primitive: absent → unset, present → set, explicit <c>null</c> → set-to-null.
/// The converter comes from the attribute on the struct — no serializer registration.
/// </summary>
public class PatchValueJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private class Payload
    {
        public PatchValue<string> Name { get; set; }
        public PatchValue<int> Stock { get; set; }
        public PatchValue<int?> Rating { get; set; }
        public PatchValue<Guid> ExternalId { get; set; }
    }

    [Test]
    public async Task AbsentProperties_DeserializeAsUnset()
    {
        var payload = JsonSerializer.Deserialize<Payload>("""{"name":"Pizzeria"}""", Web)!;

        await Assert.That(payload.Name.IsSet).IsTrue();
        await Assert.That(payload.Name.Value).IsEqualTo("Pizzeria");
        await Assert.That(payload.Stock.IsSet).IsFalse();
        await Assert.That(payload.Rating.IsSet).IsFalse();
        await Assert.That(payload.ExternalId.IsSet).IsFalse();
    }

    [Test]
    public async Task PresentProperties_DeserializeAsSetWithValue()
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Deserialize<Payload>(
            $$"""{"stock":42,"externalId":"{{id}}"}""", Web)!;

        await Assert.That(payload.Stock.IsSet).IsTrue();
        await Assert.That(payload.Stock.Value).IsEqualTo(42);
        await Assert.That(payload.ExternalId.Value).IsEqualTo(id);
    }

    [Test]
    public async Task ExplicitNull_OnNullableTarget_DeserializesAsSetToNull()
    {
        var payload = JsonSerializer.Deserialize<Payload>("""{"name":null,"rating":null}""", Web)!;

        await Assert.That(payload.Name.IsSet).IsTrue();
        await Assert.That(payload.Name.Value).IsNull();
        await Assert.That(payload.Rating.IsSet).IsTrue();
        await Assert.That(payload.Rating.Value).IsNull();
    }

    [Test]
    public async Task ExplicitNull_OnNonNullableValueType_FailsDeserialization()
    {
        // "clear" is not expressible for an int — declare PatchValue<int?> to allow it.
        await Assert.That(() => JsonSerializer.Deserialize<Payload>("""{"stock":null}""", Web))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Serialization_WritesTheInnerValue()
    {
        var json = JsonSerializer.Serialize(new Payload { Name = "Bistro", Stock = 7 }, Web);

        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Bistro");
        await Assert.That(doc.RootElement.GetProperty("stock").GetInt32()).IsEqualTo(7);
        // Unset members serialize as null — PatchValue is a request-side primitive; response
        // DTOs should not use it.
        await Assert.That(doc.RootElement.GetProperty("rating").ValueKind).IsEqualTo(JsonValueKind.Null);
    }
}
