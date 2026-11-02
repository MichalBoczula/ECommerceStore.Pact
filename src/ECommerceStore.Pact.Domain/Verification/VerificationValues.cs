using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ECommerceStore.Pact.Domain.Verification;

public enum VerificationStatus { Passed, Failed, Unverified, Error }
public enum VerificationIssueKind { Mismatch, ExecutionError }

public static class VerificationValues
{
    public static string Text(string? value, string field, int max, bool multiline = false)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max || text.Any(c => char.IsControl(c) && !(multiline && c is '\r' or '\n' or '\t')))
            throw new RegistryValidationException(field, $"Provide 1 to {max} characters without control characters.");
        return text;
    }

    public static string EvidenceUrl(string? value)
    {
        if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0)
            throw new RegistryValidationException("evidenceUrl", "Use an HTTPS URL without credentials or query parameters.");
        return uri.AbsoluteUri;
    }

    public static string Hash(params object?[] values) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));

    public static string? Json(string? value, string field)
    {
        if (value is null) return null;
        if (Encoding.UTF8.GetByteCount(value) > 8192) throw new RegistryValidationException(field, "A diagnostic JSON value is limited to 8 KiB.");
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 32 });
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer)) Write(document.RootElement, writer);
            if (buffer.Length > 8192) throw new RegistryValidationException(field, "A canonical diagnostic JSON value is limited to 8 KiB.");
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException) { throw new RegistryValidationException(field, "Provide a JSON value without duplicate properties, at most 32 levels deep."); }
    }

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) throw new JsonException();
            writer.WriteStartObject();
            foreach (var property in properties) { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) Write(item, writer);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer); // Retains number precision; never round-trip through double.
    }
}
