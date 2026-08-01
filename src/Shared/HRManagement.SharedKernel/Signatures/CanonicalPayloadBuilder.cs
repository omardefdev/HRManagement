using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HRManagement.SharedKernel.Signatures;

/// <summary>
/// Produces a deterministic SHA-256 hash of an ordered JSON representation (SIG-001 WYSIWYS).
/// </summary>
public static class CanonicalPayloadBuilder
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Serialises <paramref name="payload"/> to canonical JSON, then returns its lowercase hex SHA-256 hash.
    /// Keys are sorted alphabetically to guarantee determinism across serialiser versions.
    /// </summary>
    public static string ComputeHash(object payload)
    {
        var json = JsonSerializer.Serialize(payload, CanonicalOptions);
        var sorted = SortJsonKeys(json);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sorted));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Returns the canonical (key-sorted, minified) JSON string without hashing.</summary>
    public static string Canonicalize(object payload)
    {
        var json = JsonSerializer.Serialize(payload, CanonicalOptions);
        return SortJsonKeys(json);
    }

    private static string SortJsonKeys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            WriteSorted(doc.RootElement, writer);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteSorted(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteSorted(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteSorted(item, writer);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
