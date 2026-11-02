using System.Text.Json;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Api.Contracts;

internal static class ContractUploadReader
{
    internal const int MaxRequestBytes = 2 * 1024 * 1024;

    internal static async Task<PublishContractRequest> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType()) throw new BadHttpRequestException("JSON content is required.", 415);
        var contentType = request.GetTypedHeaders().ContentType;
        if (contentType?.Charset.HasValue == true && !string.Equals(contentType.Charset.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
            throw new BadHttpRequestException("UTF-8 JSON is required.", 415);
        if (request.ContentLength > MaxRequestBytes) throw new ContractPayloadTooLargeException();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > MaxRequestBytes) throw new ContractPayloadTooLargeException();
            buffer.Write(chunk, 0, read);
        }
        try
        {
            return JsonSerializer.Deserialize<PublishContractRequest>(buffer.ToArray(), JsonSerializerOptions.Web)
                ?? throw new JsonException();
        }
        catch (JsonException) { throw new RegistryValidationException("request", "Provide a valid JSON contract-publication request with all required fields."); }
    }
}
