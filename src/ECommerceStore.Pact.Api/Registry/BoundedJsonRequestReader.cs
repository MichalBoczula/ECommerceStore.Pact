using System.Text.Json;
using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Api.Registry;

internal sealed class RegistryRequestTooLargeException() : Exception("The JSON request envelope is too large.");

internal static class BoundedJsonRequestReader
{
    internal static async Task<T> ReadAsync<T>(HttpRequest request, int maxBytes, Func<Exception> tooLarge, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType()) throw new BadHttpRequestException("JSON content is required.", 415);
        var contentType = request.GetTypedHeaders().ContentType;
        if (contentType?.Charset.HasValue == true && !string.Equals(contentType.Charset.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
            throw new BadHttpRequestException("UTF-8 JSON is required.", 415);
        if (request.ContentLength > maxBytes) throw tooLarge();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > maxBytes) throw tooLarge();
            buffer.Write(chunk, 0, read);
        }
        try
        {
            // Reject ambiguous duplicate keys at every level before deserializing DTOs.
            using var document = JsonDocument.Parse(buffer.ToArray());
            CheckProperties(document.RootElement);
            return document.RootElement.Deserialize<T>(JsonSerializerOptions.Web) ?? throw new JsonException();
        }
        catch (JsonException) { throw new RegistryValidationException("request", "Provide a valid JSON request with all required fields and no duplicate or unknown properties."); }
    }

    private static void CheckProperties(JsonElement element, bool dto = true)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(dto ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                CheckProperties(property.Value, dto && property.Name.Equals("issues", StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckProperties(item, dto);
    }
}
